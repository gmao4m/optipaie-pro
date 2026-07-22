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
    /// <summary>One job posting as shown in the list.</summary>
    public sealed class PostingRowViewModel
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public PostingRowViewModel(JobPostingSummary summary)
        {
            Summary = summary;
        }

        public JobPostingSummary Summary { get; }
        public long Id => Summary.PostingId;
        public string Title => Summary.Title;
        public string Department => Summary.Department;
        public string StatusLabel => AtsLabels.PostingStatus(Summary.Status);
        public string OpenText => Summary.OpenDate.ToString("dd/MM/yyyy", Fr);
        public string PositionsText => Summary.Positions.ToString();
        public string CandidatesText => Summary.HiredCount + " / " + Summary.CandidateCount;
    }

    /// <summary>French labels for the recruitment enums.</summary>
    public static class AtsLabels
    {
        public static string PostingStatus(JobStatus status)
        {
            switch (status)
            {
                case JobStatus.Open: return "Ouverte";
                case JobStatus.Closed: return "Fermée";
                case JobStatus.Filled: return "Pourvue";
                default: return string.Empty;
            }
        }

        public static string Stage(CandidateStage stage)
        {
            switch (stage)
            {
                case CandidateStage.Applied: return "Candidature";
                case CandidateStage.Screening: return "Présélection";
                case CandidateStage.Interview: return "Entretien";
                case CandidateStage.Offer: return "Offre";
                case CandidateStage.Hired: return "Recruté";
                case CandidateStage.Rejected: return "Écarté";
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// Recrutement — job postings and their candidate pipeline. Hiring a candidate
    /// creates the shared employee, so the new hire flows into contracts and payroll.
    /// </summary>
    public sealed class AtsViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;

        private Company _selectedCompany;
        private PostingRowViewModel _selectedPosting;
        private string _openText = "0";
        private string _candidatesText = "0";
        private string _statusMessage = string.Empty;

        public AtsViewModel(AppServices services)
        {
            _services = services;

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selectedPosting != null);
            PipelineCommand = new RelayCommand(OpenPipeline, () => _selectedPosting != null);
            CloseCommand = new RelayCommand(() => SetStatus(JobStatus.Closed), () => _selectedPosting != null);
            ReopenCommand = new RelayCommand(() => SetStatus(JobStatus.Open), () => _selectedPosting != null);
            DeleteCommand = new RelayCommand(Delete, () => _selectedPosting != null);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<PostingRowViewModel> Postings { get; } = new ObservableCollection<PostingRowViewModel>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Load(); }
        }

        public PostingRowViewModel SelectedPosting
        {
            get => _selectedPosting;
            set => Set(ref _selectedPosting, value);
        }

        public string OpenText { get => _openText; private set => Set(ref _openText, value); }
        public string CandidatesText { get => _candidatesText; private set => Set(ref _candidatesText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand PipelineCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand ReopenCommand { get; }
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
            Postings.Clear();
            if (_selectedCompany == null)
            {
                OpenText = CandidatesText = "0";
                return;
            }

            int open = 0, candidates = 0;
            foreach (JobPostingSummary summary in _services.Ats.GetPostingsByCompany(_selectedCompany.Id))
            {
                Postings.Add(new PostingRowViewModel(summary));
                candidates += summary.CandidateCount;
                if (summary.Status == JobStatus.Open) open++;
            }

            SelectedPosting = Postings.FirstOrDefault();
            OpenText = open.ToString();
            CandidatesText = candidates.ToString();
            StatusMessage = Postings.Count + " offre(s) · " + open + " ouverte(s)";
        }

        private void New()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info("Sélectionnez d'abord une entreprise.");
                return;
            }

            ShowEditor(new AtsPostingEditViewModel(_services, _selectedCompany.Id, null));
        }

        private void Edit()
        {
            ShowEditor(new AtsPostingEditViewModel(_services, _selectedCompany.Id, _services.Ats.GetPosting(_selectedPosting.Id)));
        }

        private void ShowEditor(AtsPostingEditViewModel vm)
        {
            var window = new AtsPostingEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                Load();
                StatusMessage = "Offre enregistrée.";
            }
        }

        private void OpenPipeline()
        {
            var vm = new AtsPipelineViewModel(_services, _selectedPosting.Id, _selectedPosting.Title);
            var window = new AtsPipelineWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();

            Load(); // counts / status may have changed after a hire
        }

        private void SetStatus(JobStatus status) =>
            Run(_services.Ats.SetPostingStatus(_selectedPosting.Id, status), "Statut mis à jour.");

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer définitivement cette offre et ses candidats ?"))
            {
                return;
            }

            Run(_services.Ats.DeletePosting(_selectedPosting.Id), "Offre supprimée.");
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
