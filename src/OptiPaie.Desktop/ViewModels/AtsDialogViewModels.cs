using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>Creates or edits a job posting.</summary>
    public sealed class AtsPostingEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly JobPosting _posting;
        private readonly long _companyId;

        private string _title;
        private string _department;
        private string _description;
        private DateTime _openDate;
        private string _positions;

        public AtsPostingEditViewModel(AppServices services, long companyId, JobPosting existing)
        {
            _services = services;
            _companyId = companyId;
            _posting = existing ?? new JobPosting();

            if (existing != null)
            {
                _title = existing.Title;
                _department = existing.Department;
                _description = existing.Description;
                _openDate = existing.OpenDate;
                _positions = existing.Positions.ToString(CultureInfo.InvariantCulture);
                Title = "Modifier l'offre";
            }
            else
            {
                _openDate = DateTime.Today;
                _positions = "1";
                Title = "Nouvelle offre";
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }
        public string Title { get; }

        public string PostingTitle { get => _title; set => Set(ref _title, value); }
        public string Department { get => _department; set => Set(ref _department, value); }
        public string Description { get => _description; set => Set(ref _description, value); }
        public DateTime OpenDate { get => _openDate; set => Set(ref _openDate, value); }
        public string Positions { get => _positions; set => Set(ref _positions, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            int.TryParse(_positions, NumberStyles.Integer, CultureInfo.InvariantCulture, out int positions);
            if (positions < 1) positions = 1;

            _posting.CompanyId = _companyId;
            _posting.Title = _title;
            _posting.Department = _department;
            _posting.Description = _description;
            _posting.OpenDate = _openDate;
            _posting.Positions = positions;

            Result<long> result = _services.Ats.SavePosting(_posting);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }
    }

    /// <summary>A pipeline stage option with its French label.</summary>
    public sealed class StageOption
    {
        public StageOption(CandidateStage value) { Value = value; Label = AtsLabels.Stage(value); }
        public CandidateStage Value { get; }
        public string Label { get; }
    }

    /// <summary>One candidate row in the pipeline.</summary>
    public sealed class CandidateRowViewModel
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public CandidateRowViewModel(Candidate candidate)
        {
            Candidate = candidate;
        }

        public Candidate Candidate { get; }
        public long Id => Candidate.Id;
        public string FullName => (Candidate.LastName + " " + Candidate.FirstName).Trim();
        public string Phone => Candidate.Phone;
        public string Email => Candidate.Email;
        public string StageLabel => AtsLabels.Stage(Candidate.Stage);
        public string RatingText => Candidate.Rating > 0 ? Candidate.Rating + " / 5" : "—";
        public string AppliedText => Candidate.AppliedDate.ToString("dd/MM/yyyy", Fr);
        public bool IsHired => Candidate.Stage == CandidateStage.Hired;
    }

    /// <summary>Manages the candidate pipeline of one posting (add, move, hire, reject).</summary>
    public sealed class AtsPipelineViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _postingId;

        private CandidateRowViewModel _selectedCandidate;
        private StageOption _selectedStage;
        private string _statusMessage = string.Empty;

        public AtsPipelineViewModel(AppServices services, long postingId, string title)
        {
            _services = services;
            _postingId = postingId;
            PostingTitle = title;

            // Stages the user can move a candidate to (Hired/Rejected have their own buttons).
            foreach (CandidateStage s in new[] { CandidateStage.Applied, CandidateStage.Screening, CandidateStage.Interview, CandidateStage.Offer })
            {
                Stages.Add(new StageOption(s));
            }

            AddCommand = new RelayCommand(Add);
            MoveCommand = new RelayCommand(Move, () => _selectedCandidate != null && !_selectedCandidate.IsHired && _selectedStage != null);
            HireCommand = new RelayCommand(Hire, () => _selectedCandidate != null && !_selectedCandidate.IsHired);
            RejectCommand = new RelayCommand(Reject, () => _selectedCandidate != null && !_selectedCandidate.IsHired);
            DeleteCommand = new RelayCommand(Delete, () => _selectedCandidate != null && !_selectedCandidate.IsHired);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        public Action RequestClose { get; set; }

        public string PostingTitle { get; }

        public ObservableCollection<CandidateRowViewModel> Candidates { get; } = new ObservableCollection<CandidateRowViewModel>();
        public ObservableCollection<StageOption> Stages { get; } = new ObservableCollection<StageOption>();

        public CandidateRowViewModel SelectedCandidate
        {
            get => _selectedCandidate;
            set => Set(ref _selectedCandidate, value);
        }

        public StageOption SelectedStage
        {
            get => _selectedStage;
            set => Set(ref _selectedStage, value);
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand AddCommand { get; }
        public ICommand MoveCommand { get; }
        public ICommand HireCommand { get; }
        public ICommand RejectCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CloseCommand { get; }

        private void Load()
        {
            Candidates.Clear();
            foreach (Candidate c in _services.Ats.GetCandidates(_postingId))
            {
                Candidates.Add(new CandidateRowViewModel(c));
            }

            int hired = Candidates.Count(c => c.IsHired);
            StatusMessage = Candidates.Count + " candidat(s) · " + hired + " recruté(s)";
        }

        private void Add()
        {
            var vm = new AtsCandidateEditViewModel(_services, _postingId);
            var window = new Views.AtsCandidateEditWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                Load();
            }
        }

        private void Move()
        {
            Run(_services.Ats.MoveStage(_selectedCandidate.Id, _selectedStage.Value), "Candidat déplacé.");
        }

        private void Hire()
        {
            if (!Dialogs.Confirm("Recruter ce candidat ? Un employé sera créé dans le module Employés."))
            {
                return;
            }

            Result<HireResult> result = _services.Ats.Hire(_selectedCandidate.Id);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Load();
            StatusMessage = "Candidat recruté — l'employé a été créé" +
                            (result.Value.PostingFilled ? " et l'offre est pourvue." : ".");
        }

        private void Reject() => Run(_services.Ats.Reject(_selectedCandidate.Id), "Candidat écarté.");

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer ce candidat ?"))
            {
                return;
            }

            Run(_services.Ats.DeleteCandidate(_selectedCandidate.Id), "Candidat supprimé.");
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

    /// <summary>Creates or edits a candidate.</summary>
    public sealed class AtsCandidateEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _postingId;

        private string _lastName;
        private string _firstName;
        private string _phone;
        private string _email;
        private string _source;
        private string _rating;
        private string _notes;

        public AtsCandidateEditViewModel(AppServices services, long postingId)
        {
            _services = services;
            _postingId = postingId;
            _rating = "0";

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }

        public string LastName { get => _lastName; set => Set(ref _lastName, value); }
        public string FirstName { get => _firstName; set => Set(ref _firstName, value); }
        public string Phone { get => _phone; set => Set(ref _phone, value); }
        public string Email { get => _email; set => Set(ref _email, value); }
        public string Source { get => _source; set => Set(ref _source, value); }
        public string Rating { get => _rating; set => Set(ref _rating, value); }
        public string Notes { get => _notes; set => Set(ref _notes, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            int.TryParse(_rating, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rating);
            if (rating < 0) rating = 0;
            if (rating > 5) rating = 5;

            Result<long> result = _services.Ats.SaveCandidate(new Candidate
            {
                PostingId = _postingId,
                LastName = _lastName,
                FirstName = _firstName,
                Phone = _phone,
                Email = _email,
                Source = _source,
                Rating = rating,
                Notes = _notes,
                AppliedDate = DateTime.Today
            });

            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }
    }
}
