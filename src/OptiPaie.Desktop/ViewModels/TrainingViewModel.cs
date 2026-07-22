using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One training session as shown in the list.</summary>
    public sealed class TrainingRowViewModel
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public TrainingRowViewModel(TrainingSummary summary)
        {
            Summary = summary;
        }

        public TrainingSummary Summary { get; }
        public long Id => Summary.SessionId;
        public string Title => Summary.Title;
        public string Category => Summary.Category;
        public string Provider => Summary.Provider;
        public string StatusLabel => TrainingLabels.Status(Summary.Status);
        public string DatesText => Summary.StartDate.ToString("dd/MM/yyyy", Fr) +
                                   (Summary.EndDate.HasValue ? " → " + Summary.EndDate.Value.ToString("dd/MM/yyyy", Fr) : string.Empty);
        public string CostText => Summary.Cost.ToString("N2", Fr);
        public string ParticipantsText => Summary.CompletedCount + " / " + Summary.ParticipantCount;

        /// <summary>Semantic colour bucket for the status pill (matches the Contracts grid).</summary>
        public string StatusKind
        {
            get
            {
                switch (Summary.Status)
                {
                    case TrainingStatus.Ongoing: return "accent";    // in progress
                    case TrainingStatus.Completed: return "success"; // done
                    case TrainingStatus.Cancelled: return "danger";  // cancelled
                    default: return "neutral";                       // planned
                }
            }
        }
    }

    /// <summary>French labels for the training enums.</summary>
    public static class TrainingLabels
    {
        private static string L(string key) => OptiPaie.Desktop.Localization.TranslationSource.Instance[key];

        public static string Status(TrainingStatus status)
        {
            switch (status)
            {
                case TrainingStatus.Planned: return L("Enum_TrainingStatus_Planned");
                case TrainingStatus.Ongoing: return L("Enum_TrainingStatus_Ongoing");
                case TrainingStatus.Completed: return L("Enum_TrainingStatus_Completed");
                case TrainingStatus.Cancelled: return L("Enum_TrainingStatus_Cancelled");
                default: return string.Empty;
            }
        }

        public static string Result(TrainingResult result)
        {
            switch (result)
            {
                case TrainingResult.Enrolled: return L("Enum_TrainingResult_Enrolled");
                case TrainingResult.Completed: return L("Enum_TrainingResult_Completed");
                case TrainingResult.Failed: return L("Enum_TrainingResult_Failed");
                case TrainingResult.Absent: return L("Enum_TrainingResult_Absent");
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// Formation — training sessions of a company and their participants. Participants
    /// are drawn from the shared employees, so enrolments always point at the live record.
    /// </summary>
    public sealed class TrainingViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;

        private Company _selectedCompany;
        private TrainingRowViewModel _selectedSession;
        private string _plannedText = "0";
        private string _totalCostText = "0";
        private string _statusMessage = string.Empty;

        public TrainingViewModel(AppServices services)
        {
            _services = services;

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selectedSession != null);
            ParticipantsCommand = new RelayCommand(OpenParticipants, () => _selectedSession != null);
            OngoingCommand = new RelayCommand(() => SetStatus(TrainingStatus.Ongoing), () => _selectedSession != null);
            CompleteCommand = new RelayCommand(() => SetStatus(TrainingStatus.Completed), () => _selectedSession != null);
            CancelSessionCommand = new RelayCommand(() => SetStatus(TrainingStatus.Cancelled), () => _selectedSession != null);
            DeleteCommand = new RelayCommand(Delete, () => _selectedSession != null);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<TrainingRowViewModel> Sessions { get; } = new ObservableCollection<TrainingRowViewModel>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Load(); }
        }

        public TrainingRowViewModel SelectedSession
        {
            get => _selectedSession;
            set => Set(ref _selectedSession, value);
        }

        public string PlannedText { get => _plannedText; private set => Set(ref _plannedText, value); }
        public string TotalCostText { get => _totalCostText; private set => Set(ref _totalCostText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ParticipantsCommand { get; }
        public ICommand OngoingCommand { get; }
        public ICommand CompleteCommand { get; }
        public ICommand CancelSessionCommand { get; }
        public ICommand DeleteCommand { get; }

        public void OnActivated()
        {
            // The active company comes from the single global selector in the header.
            _selectedCompany = _services.CompanyContext.Active;
            Raise(nameof(SelectedCompany));
            Load();
        }

        private void Load()
        {
            Sessions.Clear();
            if (_selectedCompany == null)
            {
                PlannedText = TotalCostText = "0";
                return;
            }

            int planned = 0;
            decimal totalCost = 0m;

            foreach (TrainingSummary summary in _services.Training.GetByCompany(_selectedCompany.Id))
            {
                Sessions.Add(new TrainingRowViewModel(summary));
                totalCost += summary.Cost;
                if (summary.Status == TrainingStatus.Planned || summary.Status == TrainingStatus.Ongoing) planned++;
            }

            SelectedSession = Sessions.FirstOrDefault();
            PlannedText = planned.ToString();
            TotalCostText = totalCost.ToString("N2", Fr);
            StatusMessage = Sessions.Count + " formation(s)";
        }

        private void New()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info("Sélectionnez d'abord une entreprise.");
                return;
            }

            ShowEditor(new TrainingEditViewModel(_services, _selectedCompany.Id, null));
        }

        private void Edit()
        {
            ShowEditor(new TrainingEditViewModel(_services, _selectedCompany.Id, _services.Training.Get(_selectedSession.Id)));
        }

        private void ShowEditor(TrainingEditViewModel vm)
        {
            var window = new TrainingEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                Load();
                StatusMessage = "Formation enregistrée.";
            }
        }

        private void OpenParticipants()
        {
            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            var vm = new TrainingParticipantsViewModel(_services, _selectedSession.Id, _selectedSession.Title, employees);
            var window = new TrainingParticipantsWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();

            Load(); // counts may have changed
        }

        private void SetStatus(TrainingStatus status) =>
            Run(_services.Training.SetStatus(_selectedSession.Id, status), "Statut mis à jour.");

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer définitivement cette formation et ses inscriptions ?"))
            {
                return;
            }

            Run(_services.Training.Delete(_selectedSession.Id), "Formation supprimée.");
        }

        private void Run(Result result, string success)
        {
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Load();
            StatusMessage = success;
        }
    }
}
