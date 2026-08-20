using System;
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
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>Arabic labels for the recruitment enums.</summary>
    public static class AtsLabels
    {
        private static string L(string key) => TranslationSource.Instance[key];

        public static string PostingStatus(JobStatus status)
        {
            switch (status)
            {
                case JobStatus.Open: return L("Enum_JobStatus_Open");
                case JobStatus.Closed: return L("Enum_JobStatus_Closed");
                case JobStatus.Filled: return L("Enum_JobStatus_Filled");
                default: return string.Empty;
            }
        }

        public static string Stage(CandidateStage stage)
        {
            switch (stage)
            {
                case CandidateStage.Applied: return L("Enum_CandidateStage_Applied");
                case CandidateStage.Screening: return L("Enum_CandidateStage_Screening");
                case CandidateStage.Interview: return L("Enum_CandidateStage_Interview");
                case CandidateStage.Offer: return L("Enum_CandidateStage_Offer");
                case CandidateStage.Hired: return L("Enum_CandidateStage_Hired");
                case CandidateStage.Rejected: return L("Enum_CandidateStage_Rejected");
                default: return string.Empty;
            }
        }
    }

    /// <summary>One job posting in the left list.</summary>
    public sealed class PostingRowViewModel
    {
        public PostingRowViewModel(JobPostingSummary summary) { Summary = summary; }

        public JobPostingSummary Summary { get; }
        public long Id => Summary.PostingId;
        public string Title => Summary.Title;
        public string Department => Summary.Department;
        public string StatusLabel => AtsLabels.PostingStatus(Summary.Status);
        public string CountText => Summary.CandidateCount.ToString();
        public bool IsOpen => Summary.Status == JobStatus.Open;

        public string StatusKind
        {
            get
            {
                switch (Summary.Status)
                {
                    case JobStatus.Open: return "accent";
                    case JobStatus.Filled: return "success";
                    default: return "neutral";
                }
            }
        }
    }

    /// <summary>One candidate in the right list — carries the single "next step" it offers.</summary>
    public sealed class CandidateRowViewModel
    {
        private static string L(string key) => TranslationSource.Instance[key];

        public CandidateRowViewModel(Candidate candidate) { Candidate = candidate; }

        public Candidate Candidate { get; }
        public long Id => Candidate.Id;
        public string FullName => (Candidate.LastName + " " + Candidate.FirstName).Trim();
        public string Phone => Candidate.Phone;
        public string StageLabel => AtsLabels.Stage(Candidate.Stage);

        public bool IsClosed => Candidate.Stage == CandidateStage.Hired || Candidate.Stage == CandidateStage.Rejected;

        /// <summary>The single "next step" button — only for a candidate still in the pipeline.</summary>
        public bool ShowNextAction => !IsClosed;

        public string NextActionLabel
        {
            get
            {
                switch (Candidate.Stage)
                {
                    case CandidateStage.Applied: return "← " + AtsLabels.Stage(CandidateStage.Screening);
                    case CandidateStage.Screening: return "← " + AtsLabels.Stage(CandidateStage.Interview);
                    case CandidateStage.Interview: return "← " + AtsLabels.Stage(CandidateStage.Offer);
                    case CandidateStage.Offer: return L("Recruit_Hire");   // « توظيف »
                    default: return string.Empty;
                }
            }
        }

        public bool CanBack => Candidate.Stage == CandidateStage.Screening ||
                               Candidate.Stage == CandidateStage.Interview ||
                               Candidate.Stage == CandidateStage.Offer;

        /// <summary>Closed files show why, in one line.</summary>
        public string ClosureLabel
        {
            get
            {
                if (Candidate.Stage != CandidateStage.Rejected) return string.Empty;
                string kind = Candidate.ClosureType == CandidateClosure.Withdrawn ? L("Recruit_Withdrawn") : L("Recruit_Rejected");
                return string.IsNullOrWhiteSpace(Candidate.ClosureReason) ? kind : kind + " — " + Candidate.ClosureReason;
            }
        }

        public string StageKind
        {
            get
            {
                switch (Candidate.Stage)
                {
                    case CandidateStage.Hired: return "success";
                    case CandidateStage.Rejected: return "neutral";
                    default: return "accent";
                }
            }
        }
    }

    /// <summary>
    /// Recrutement — ONE screen: postings on the left, the selected posting's candidates on the
    /// right. Everything opens on a direct click; only the two "+" buttons are always visible,
    /// every other action lives in a right-click menu. Advancing a candidate is a single
    /// "étape suivante" click. Hiring creates the shared employee (see <see cref="IAtsService"/>).
    /// </summary>
    public sealed class AtsViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;

        private Company _company;
        private PostingRowViewModel _selectedPosting;
        private CandidateRowViewModel _selectedCandidate;
        private string _statusMessage = string.Empty;

        public AtsViewModel(AppServices services)
        {
            _services = services;

            NewPostingCommand = new RelayCommand(NewPosting);
            NewCandidateCommand = new RelayCommand(NewCandidate, () => _selectedPosting != null);

            OpenPostingCommand = new RelayCommand(p => OpenPosting(p as PostingRowViewModel));
            EditPostingCommand = new RelayCommand(p => EditPosting(p as PostingRowViewModel));
            TogglePostingCommand = new RelayCommand(p => TogglePosting(p as PostingRowViewModel));
            DeletePostingCommand = new RelayCommand(p => DeletePosting(p as PostingRowViewModel));

            OpenCandidateCommand = new RelayCommand(p => OpenCandidate(p as CandidateRowViewModel));
            NextStepCommand = new RelayCommand(p => NextStep(p as CandidateRowViewModel));
            BackStepCommand = new RelayCommand(p => BackStep(p as CandidateRowViewModel));
            RejectCommand = new RelayCommand(p => Close(p as CandidateRowViewModel, withdrawal: false));
            DesistCommand = new RelayCommand(p => Close(p as CandidateRowViewModel, withdrawal: true));
            DeleteCandidateCommand = new RelayCommand(p => DeleteCandidate(p as CandidateRowViewModel));
        }

        public ObservableCollection<PostingRowViewModel> Postings { get; } = new ObservableCollection<PostingRowViewModel>();
        public ObservableCollection<CandidateRowViewModel> Candidates { get; } = new ObservableCollection<CandidateRowViewModel>();

        public PostingRowViewModel SelectedPosting
        {
            get => _selectedPosting;
            set { if (Set(ref _selectedPosting, value)) { Raise(nameof(HasSelectedPosting)); Raise(nameof(CandidatesHeader)); LoadCandidates(); } }
        }

        public CandidateRowViewModel SelectedCandidate
        {
            get => _selectedCandidate;
            set => Set(ref _selectedCandidate, value);
        }

        public bool HasSelectedPosting => _selectedPosting != null;
        public string CandidatesHeader => _selectedPosting != null ? _selectedPosting.Title : string.Empty;
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        // Two visible buttons only; everything else is a right-click action.
        public ICommand NewPostingCommand { get; }
        public ICommand NewCandidateCommand { get; }
        public ICommand OpenPostingCommand { get; }
        public ICommand EditPostingCommand { get; }
        public ICommand TogglePostingCommand { get; }
        public ICommand DeletePostingCommand { get; }
        public ICommand OpenCandidateCommand { get; }
        public ICommand NextStepCommand { get; }
        public ICommand BackStepCommand { get; }
        public ICommand RejectCommand { get; }
        public ICommand DesistCommand { get; }
        public ICommand DeleteCandidateCommand { get; }

        public void OnActivated()
        {
            _company = _services.CompanyContext.Active;
            LoadPostings();
        }

        private void LoadPostings()
        {
            Postings.Clear();
            Candidates.Clear();
            if (_company == null)
            {
                StatusMessage = L("Recruit_NeedCompany");
                return;
            }

            foreach (JobPostingSummary s in _services.Ats.GetPostingsByCompany(_company.Id))
            {
                Postings.Add(new PostingRowViewModel(s));
            }

            SelectedPosting = Postings.FirstOrDefault();
            int open = Postings.Count(p => p.IsOpen);
            StatusMessage = string.Format(L("Recruit_PostingsSummary"), Postings.Count, open);
        }

        private void LoadCandidates()
        {
            Candidates.Clear();
            if (_selectedPosting == null) return;
            foreach (Candidate c in _services.Ats.GetCandidates(_selectedPosting.Id))
            {
                Candidates.Add(new CandidateRowViewModel(c));
            }
        }

        // -- postings ---------------------------------------------------------

        private void NewPosting()
        {
            if (_company == null) { Dialogs.Info(L("Recruit_NeedCompany")); return; }
            if (ShowPostingEditor(new AtsPostingEditViewModel(_services, _company.Id, null))) LoadPostings();
        }

        private void EditPosting(PostingRowViewModel row)
        {
            row = row ?? _selectedPosting;
            if (row == null) return;
            if (ShowPostingEditor(new AtsPostingEditViewModel(_services, _company.Id, _services.Ats.GetPosting(row.Id)))) LoadPostings();
        }

        private void OpenPosting(PostingRowViewModel row)
        {
            if (row != null) SelectedPosting = row;   // a direct click selects + loads candidates
        }

        private bool ShowPostingEditor(AtsPostingEditViewModel vm)
        {
            var window = new AtsPostingEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }

        private void TogglePosting(PostingRowViewModel row)
        {
            row = row ?? _selectedPosting;
            if (row == null) return;
            JobStatus target = row.IsOpen ? JobStatus.Closed : JobStatus.Open;
            Run(_services.Ats.SetPostingStatus(row.Id, target), reloadPostings: true);
        }

        private void DeletePosting(PostingRowViewModel row)
        {
            row = row ?? _selectedPosting;
            if (row == null) return;
            if (!Dialogs.Confirm(L("Recruit_ConfirmDeletePosting"))) return;
            Run(_services.Ats.DeletePosting(row.Id), reloadPostings: true);
        }

        // -- candidates -------------------------------------------------------

        private void NewCandidate()
        {
            if (_selectedPosting == null) return;
            var vm = new AtsCandidateEditViewModel(_services, _selectedPosting.Id, null);
            if (ShowCandidateEditor(vm)) ReloadKeepingPosting();
        }

        private void OpenCandidate(CandidateRowViewModel row)
        {
            row = row ?? _selectedCandidate;
            if (row == null) return;
            var vm = new AtsCandidateEditViewModel(_services, _selectedPosting.Id, _services.Ats.GetCandidate(row.Id));
            if (ShowCandidateEditor(vm)) ReloadKeepingPosting();
        }

        private bool ShowCandidateEditor(AtsCandidateEditViewModel vm)
        {
            var window = new AtsCandidateEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }

        /// <summary>The single "étape suivante" action — advances one step, or hires at « Retenu ».</summary>
        private void NextStep(CandidateRowViewModel row)
        {
            row = row ?? _selectedCandidate;
            if (row == null || row.IsClosed) return;

            if (row.Candidate.Stage == CandidateStage.Offer)
            {
                if (!Dialogs.Confirm(L("Recruit_ConfirmHire"))) return;

                Result<HireResult> hired = _services.Ats.Hire(row.Id);
                if (hired.IsFailure) { Fail(hired.Error, hired.ErrorCode); return; }

                ReloadKeepingPosting();
                LoadPostings(); // a hire may fill the posting
                StatusMessage = hired.Value.PostingFilled ? L("Recruit_HiredAndFilled") : L("Recruit_Hired");
                return;
            }

            Run(_services.Ats.MoveNext(row.Id), reloadPostings: false);
        }

        private void BackStep(CandidateRowViewModel row)
        {
            row = row ?? _selectedCandidate;
            if (row == null || !row.CanBack) return;
            if (!Dialogs.Confirm(L("Recruit_ConfirmBack"))) return;
            Run(_services.Ats.MoveBack(row.Id), reloadPostings: false);
        }

        private void Close(CandidateRowViewModel row, bool withdrawal)
        {
            row = row ?? _selectedCandidate;
            if (row == null || row.IsClosed) return;

            string title = withdrawal ? L("Recruit_Desist") : L("Recruit_Reject");
            string reason = Dialogs.Prompt(title, L("Recruit_ReasonLabel"), null, required: true);
            if (string.IsNullOrWhiteSpace(reason)) return;   // cancelled — nothing changes

            Result result = withdrawal ? _services.Ats.Desist(row.Id, reason) : _services.Ats.Reject(row.Id, reason);
            Run(result, reloadPostings: false);
        }

        private void DeleteCandidate(CandidateRowViewModel row)
        {
            row = row ?? _selectedCandidate;
            if (row == null) return;
            if (!Dialogs.Confirm(L("Recruit_ConfirmDeleteCandidate"))) return;
            Run(_services.Ats.DeleteCandidate(row.Id), reloadPostings: false);
        }

        // -- helpers ----------------------------------------------------------

        private void ReloadKeepingPosting()
        {
            long? id = _selectedPosting?.Id;
            LoadPostings();
            if (id.HasValue)
            {
                SelectedPosting = Postings.FirstOrDefault(p => p.Id == id.Value) ?? Postings.FirstOrDefault();
            }
        }

        private void Run(Result result, bool reloadPostings)
        {
            if (result.IsFailure) { Fail(result.Error, result.ErrorCode); return; }
            if (reloadPostings) LoadPostings(); else ReloadKeepingPosting();
        }

        private void Fail(string error, string errorCode)
        {
            string message = ResultText.Localize(_services.Localization, error, errorCode);
            _services.Logger.Warn("Recrutement: " + errorCode + " — " + error);   // no silent failure
            Dialogs.Error(message);
        }

        private static string L(string key) => TranslationSource.Instance[key];
    }
}
