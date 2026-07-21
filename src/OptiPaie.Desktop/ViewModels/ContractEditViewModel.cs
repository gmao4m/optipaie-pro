using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>A contract type with its French label (for the combo box).</summary>
    public sealed class ContractTypeOption
    {
        public ContractTypeOption(ContractType value) { Value = value; Label = EnumLabels.ContractLabel(value); }
        public ContractType Value { get; }
        public string Label { get; }
    }

    /// <summary>
    /// Creates or edits a draft contract. A CDI hides the end date. A contract already
    /// in force is edited only for its reference/notes (enforced by the service).
    /// </summary>
    public sealed class ContractEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly EmploymentContract _contract;

        private Employee _selectedEmployee;
        private ContractTypeOption _selectedType;
        private string _reference;
        private string _position;
        private string _baseSalary;
        private DateTime _startDate;
        private DateTime? _endDate;
        private string _trialDays;
        private DateTime? _signedDate;
        private string _notes;

        public ContractEditViewModel(AppServices services, IReadOnlyList<Employee> employees, EmploymentContract existing)
        {
            _services = services;
            _contract = existing ?? new EmploymentContract();

            foreach (Employee employee in employees) Employees.Add(employee);
            foreach (ContractType type in Enum.GetValues(typeof(ContractType))) Types.Add(new ContractTypeOption(type));

            if (existing != null)
            {
                _selectedEmployee = FindEmployee(existing.EmployeeId);
                _selectedType = FindType(existing.Type);
                _reference = existing.Reference;
                _position = existing.Position;
                _baseSalary = existing.BaseSalary.ToString(CultureInfo.InvariantCulture);
                _startDate = existing.StartDate;
                _endDate = existing.EndDate;
                _trialDays = existing.TrialPeriodDays.ToString(CultureInfo.InvariantCulture);
                _signedDate = existing.SignedDate;
                _notes = existing.Notes;
                Title = "Modifier le contrat";
            }
            else
            {
                _selectedEmployee = Employees.Count > 0 ? Employees[0] : null;
                _selectedType = FindType(ContractType.Cdi);
                _startDate = DateTime.Today;
                _trialDays = "0";
                Title = "Nouveau contrat";
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }

        public string Title { get; }

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<ContractTypeOption> Types { get; } = new ObservableCollection<ContractTypeOption>();

        public bool CanChooseEmployee => _contract.Id == 0;

        public Employee SelectedEmployee { get => _selectedEmployee; set => Set(ref _selectedEmployee, value); }

        public ContractTypeOption SelectedType
        {
            get => _selectedType;
            set { if (Set(ref _selectedType, value)) Raise(nameof(ShowEndDate)); }
        }

        /// <summary>A fixed-term contract needs an end date; a CDI does not.</summary>
        public bool ShowEndDate => _selectedType != null && _selectedType.Value != ContractType.Cdi;

        public string Reference { get => _reference; set => Set(ref _reference, value); }
        public string Position { get => _position; set => Set(ref _position, value); }
        public string BaseSalary { get => _baseSalary; set => Set(ref _baseSalary, value); }
        public DateTime StartDate { get => _startDate; set => Set(ref _startDate, value); }
        public DateTime? EndDate { get => _endDate; set => Set(ref _endDate, value); }
        public string TrialPeriodDays { get => _trialDays; set => Set(ref _trialDays, value); }
        public DateTime? SignedDate { get => _signedDate; set => Set(ref _signedDate, value); }
        public string Notes { get => _notes; set => Set(ref _notes, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            if (_selectedEmployee == null)
            {
                Dialogs.Error("Sélectionnez un employé.");
                return;
            }

            if (_selectedType == null)
            {
                Dialogs.Error("Sélectionnez un type de contrat.");
                return;
            }

            if (!decimal.TryParse((_baseSalary ?? string.Empty).Replace(" ", "").Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out decimal salary))
            {
                Dialogs.Error("Salaire de base invalide.");
                return;
            }

            int.TryParse(_trialDays, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trial);

            _contract.EmployeeId = _selectedEmployee.Id;
            _contract.Type = _selectedType.Value;
            _contract.Reference = _reference;
            _contract.Position = _position;
            _contract.BaseSalary = salary;
            _contract.StartDate = _startDate;
            _contract.EndDate = ShowEndDate ? _endDate : null;
            _contract.TrialPeriodDays = trial;
            _contract.SignedDate = _signedDate;
            _contract.Notes = _notes;

            Result<long> result = _services.Contracts.Save(_contract);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }

        private Employee FindEmployee(long id)
        {
            foreach (Employee employee in Employees)
            {
                if (employee.Id == id) return employee;
            }

            return null;
        }

        private ContractTypeOption FindType(ContractType type)
        {
            foreach (ContractTypeOption option in Types)
            {
                if (option.Value == type) return option;
            }

            return null;
        }
    }

    /// <summary>Small dialog view model for terminating a contract.</summary>
    public sealed class ContractTerminateViewModel : ObservableObject
    {
        private DateTime _effectiveDate = DateTime.Today;
        private string _reason;

        public ContractTerminateViewModel()
        {
            ConfirmCommand = new RelayCommand(() => RequestClose?.Invoke(true));
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }

        public DateTime EffectiveDate { get => _effectiveDate; set => Set(ref _effectiveDate, value); }
        public string Reason { get => _reason; set => Set(ref _reason, value); }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
    }

    /// <summary>Small dialog view model for renewing a contract.</summary>
    public sealed class ContractRenewViewModel : ObservableObject
    {
        private DateTime _newStart;
        private DateTime? _newEnd;
        private string _newSalaryText;
        private readonly bool _isFixedTerm;

        public ContractRenewViewModel(EmploymentContract current)
        {
            _isFixedTerm = current.Type != ContractType.Cdi;
            _newStart = current.EndDate.HasValue ? current.EndDate.Value.AddDays(1) : DateTime.Today;
            _newEnd = _isFixedTerm ? (DateTime?)_newStart.AddYears(1) : null;
            _newSalaryText = current.BaseSalary.ToString(CultureInfo.InvariantCulture);

            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }

        public bool ShowEndDate => _isFixedTerm;

        public DateTime NewStart { get => _newStart; set => Set(ref _newStart, value); }
        public DateTime? NewEnd { get => _newEnd; set => Set(ref _newEnd, value); }
        public string NewSalaryText { get => _newSalaryText; set => Set(ref _newSalaryText, value); }

        public decimal NewSalary { get; private set; }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        private void Confirm()
        {
            if (!decimal.TryParse((_newSalaryText ?? string.Empty).Replace(" ", "").Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out decimal salary) || salary <= 0m)
            {
                Dialogs.Error("Salaire invalide.");
                return;
            }

            if (_isFixedTerm && !_newEnd.HasValue)
            {
                Dialogs.Error("Un contrat à durée déterminée doit avoir une date de fin.");
                return;
            }

            NewSalary = salary;
            RequestClose?.Invoke(true);
        }
    }
}
