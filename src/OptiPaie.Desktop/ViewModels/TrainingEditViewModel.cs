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
    /// <summary>Creates or edits a training session (status is driven separately).</summary>
    public sealed class TrainingEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly TrainingSession _session;
        private readonly long _companyId;

        private string _title;
        private string _category;
        private string _provider;
        private string _location;
        private DateTime _startDate;
        private DateTime? _endDate;
        private string _cost;
        private string _notes;

        public TrainingEditViewModel(AppServices services, long companyId, TrainingSession existing)
        {
            _services = services;
            _companyId = companyId;
            _session = existing ?? new TrainingSession();

            if (existing != null)
            {
                _title = existing.Title;
                _category = existing.Category;
                _provider = existing.Provider;
                _location = existing.Location;
                _startDate = existing.StartDate;
                _endDate = existing.EndDate;
                _cost = existing.Cost.ToString(CultureInfo.InvariantCulture);
                _notes = existing.Notes;
                Title = "Modifier la formation";
            }
            else
            {
                _startDate = DateTime.Today;
                _cost = "0";
                Title = "Nouvelle formation";
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }
        public string Title { get; }

        public string SessionTitle { get => _title; set => Set(ref _title, value); }
        public string Category { get => _category; set => Set(ref _category, value); }
        public string Provider { get => _provider; set => Set(ref _provider, value); }
        public string Location { get => _location; set => Set(ref _location, value); }
        public DateTime StartDate { get => _startDate; set => Set(ref _startDate, value); }
        public DateTime? EndDate { get => _endDate; set => Set(ref _endDate, value); }
        public string Cost { get => _cost; set => Set(ref _cost, value); }
        public string Notes { get => _notes; set => Set(ref _notes, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            if (!decimal.TryParse((_cost ?? string.Empty).Replace(" ", "").Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cost))
            {
                Dialogs.Error("Coût invalide.");
                return;
            }

            _session.CompanyId = _companyId;
            _session.Title = _title;
            _session.Category = _category;
            _session.Provider = _provider;
            _session.Location = _location;
            _session.StartDate = _startDate;
            _session.EndDate = _endDate;
            _session.Cost = cost;
            _session.Notes = _notes;

            Result<long> result = _services.Training.Save(_session);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }
    }

    /// <summary>A training result option with its French label.</summary>
    public sealed class TrainingResultOption
    {
        public TrainingResultOption(TrainingResult value) { Value = value; Label = TrainingLabels.Result(value); }
        public TrainingResult Value { get; }
        public string Label { get; }
    }

    /// <summary>One participant row in the participants manager.</summary>
    public sealed class TrainingParticipantRowViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private TrainingResultOption _result;
        private string _score;
        private string _certificateRef;

        public TrainingParticipantRowViewModel(AppServices services, TrainingParticipantSummary summary, IEnumerable<TrainingResultOption> options)
        {
            _services = services;
            Summary = summary;
            foreach (TrainingResultOption o in options) Results.Add(o);
            _result = Results.FirstOrDefault(o => o.Value == summary.Result);
            _score = summary.Score;
            _certificateRef = summary.CertificateRef;
        }

        public TrainingParticipantSummary Summary { get; }
        public long Id => Summary.ParticipantId;
        public string EmployeeName => Summary.EmployeeName;

        public ObservableCollection<TrainingResultOption> Results { get; } = new ObservableCollection<TrainingResultOption>();

        public TrainingResultOption Result
        {
            get => _result;
            set { if (Set(ref _result, value)) Persist(); }
        }

        public string Score
        {
            get => _score;
            set { if (Set(ref _score, value)) Persist(); }
        }

        public string CertificateRef
        {
            get => _certificateRef;
            set { if (Set(ref _certificateRef, value)) Persist(); }
        }

        private void Persist()
        {
            _services.Training.SetResult(Summary.ParticipantId,
                _result != null ? _result.Value : TrainingResult.Enrolled, _score, _certificateRef);
        }
    }

    /// <summary>Manages the participants of one session: enrol, record outcomes, remove.</summary>
    public sealed class TrainingParticipantsViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _sessionId;
        private readonly List<TrainingResultOption> _resultOptions;

        private Employee _selectedEmployee;
        private TrainingParticipantRowViewModel _selectedParticipant;
        private string _statusMessage = string.Empty;

        public TrainingParticipantsViewModel(AppServices services, long sessionId, string title, IReadOnlyList<Employee> employees)
        {
            _services = services;
            _sessionId = sessionId;
            SessionTitle = title;

            _resultOptions = Enum.GetValues(typeof(TrainingResult)).Cast<TrainingResult>()
                .Select(r => new TrainingResultOption(r)).ToList();

            foreach (Employee e in employees) Employees.Add(e);
            _selectedEmployee = Employees.FirstOrDefault();

            EnrollCommand = new RelayCommand(Enroll);
            RemoveCommand = new RelayCommand(Remove, () => _selectedParticipant != null);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        public Action RequestClose { get; set; }

        public string SessionTitle { get; }

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<TrainingParticipantRowViewModel> Participants { get; } = new ObservableCollection<TrainingParticipantRowViewModel>();

        public Employee SelectedEmployee { get => _selectedEmployee; set => Set(ref _selectedEmployee, value); }

        public TrainingParticipantRowViewModel SelectedParticipant
        {
            get => _selectedParticipant;
            set => Set(ref _selectedParticipant, value);
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand EnrollCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand CloseCommand { get; }

        private void Load()
        {
            Participants.Clear();
            foreach (TrainingParticipantSummary p in _services.Training.GetParticipants(_sessionId))
            {
                Participants.Add(new TrainingParticipantRowViewModel(_services, p, _resultOptions));
            }

            StatusMessage = Participants.Count + " participant(s)";
        }

        private void Enroll()
        {
            if (_selectedEmployee == null)
            {
                Dialogs.Info("Sélectionnez un employé.");
                return;
            }

            Result result = _services.Training.Enroll(_sessionId, _selectedEmployee.Id);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Load();
        }

        private void Remove()
        {
            _services.Training.RemoveParticipant(_selectedParticipant.Id);
            Load();
        }
    }
}
