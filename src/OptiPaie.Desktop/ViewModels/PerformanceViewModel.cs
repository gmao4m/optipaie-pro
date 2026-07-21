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
    /// <summary>One review as shown in the list.</summary>
    public sealed class PerformanceRowViewModel
    {
        public PerformanceRowViewModel(PerformanceSummary summary)
        {
            Summary = summary;
        }

        public PerformanceSummary Summary { get; }
        public long Id => Summary.ReviewId;
        public string EmployeeName => Summary.EmployeeName;
        public string PeriodLabel => Summary.PeriodLabel;
        public string Reviewer => Summary.Reviewer;
        public string DateText => Summary.ReviewDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"));
        public string ScoreText => Summary.OverallScore.ToString("0.##", CultureInfo.InvariantCulture) + " / 20";
        public string Rating => Summary.Rating;
        public string StatusLabel => Summary.Status == PerformanceStatus.Completed ? "Finalisée" : "Brouillon";
        public bool IsDraft => Summary.Status == PerformanceStatus.Draft;
        public bool IsCompleted => Summary.Status == PerformanceStatus.Completed;
    }

    /// <summary>
    /// Évaluations — performance reviews of a company for a year. A review pulls the
    /// employee's attendance (absences, retards) live from the Attendance module, so it
    /// always reflects the latest presence data.
    /// </summary>
    public sealed class PerformanceViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;

        private Company _selectedCompany;
        private int _selectedYear = DateTime.Today.Year;
        private PerformanceRowViewModel _selectedReview;
        private string _statusMessage = string.Empty;

        public PerformanceViewModel(AppServices services)
        {
            _services = services;

            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);

            NewCommand = new RelayCommand(New);
            OpenCommand = new RelayCommand(Open, () => _selectedReview != null);
            CompleteCommand = new RelayCommand(CompleteReview, () => _selectedReview != null && _selectedReview.IsDraft);
            ReopenCommand = new RelayCommand(Reopen, () => _selectedReview != null && _selectedReview.IsCompleted);
            DeleteCommand = new RelayCommand(Delete, () => _selectedReview != null);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<PerformanceRowViewModel> Reviews { get; } = new ObservableCollection<PerformanceRowViewModel>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Load(); }
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set { if (Set(ref _selectedYear, value)) Load(); }
        }

        public PerformanceRowViewModel SelectedReview
        {
            get => _selectedReview;
            set => Set(ref _selectedReview, value);
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand CompleteCommand { get; }
        public ICommand ReopenCommand { get; }
        public ICommand DeleteCommand { get; }

        public void OnActivated()
        {
            IReadOnlyList<Company> companies = _services.Companies.GetAll();
            Companies.Clear();
            foreach (Company c in companies) Companies.Add(c);

            if (_selectedCompany == null && Companies.Count > 0)
            {
                SelectedCompany = Companies[0]; // triggers Load
            }
            else
            {
                Load();
            }
        }

        private void Load()
        {
            Reviews.Clear();
            if (_selectedCompany == null) return;

            foreach (PerformanceSummary summary in _services.Performance.GetByCompanyYear(_selectedCompany.Id, _selectedYear))
            {
                Reviews.Add(new PerformanceRowViewModel(summary));
            }

            SelectedReview = Reviews.FirstOrDefault();
            int completed = Reviews.Count(r => r.IsCompleted);
            StatusMessage = Reviews.Count + " évaluation(s) · " + completed + " finalisée(s)";
        }

        private void New()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info("Sélectionnez d'abord une entreprise.");
                return;
            }

            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            if (employees.Count == 0)
            {
                Dialogs.Info("Aucun employé actif dans cette entreprise.");
                return;
            }

            var vm = new PerformanceEditViewModel(_services, employees, _selectedYear, 0);
            if (ShowEditor(vm))
            {
                Load();
            }
        }

        private void Open()
        {
            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            var vm = new PerformanceEditViewModel(_services, employees, _selectedYear, _selectedReview.Id);
            if (ShowEditor(vm))
            {
                Load();
            }
        }

        private bool ShowEditor(PerformanceEditViewModel vm)
        {
            var window = new PerformanceEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }

        private void CompleteReview() => Run(_services.Performance.Complete(_selectedReview.Id), "Évaluation finalisée.");
        private void Reopen() => Run(_services.Performance.Reopen(_selectedReview.Id), "Évaluation rouverte.");

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer définitivement cette évaluation ?"))
            {
                return;
            }

            Run(_services.Performance.Delete(_selectedReview.Id), "Évaluation supprimée.");
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
