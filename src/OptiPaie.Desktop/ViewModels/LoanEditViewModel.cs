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
    /// <summary>A loan type with its French label (for the combo box).</summary>
    public sealed class LoanTypeOption
    {
        public LoanTypeOption(LoanType value) { Value = value; Label = LoanLabels.Type(value); }
        public LoanType Value { get; }
        public string Label { get; }
    }

    /// <summary>
    /// Creates or edits a loan. The instalment count is shown live so the grant is
    /// never ambiguous. The employee cannot change once the loan exists.
    /// </summary>
    public sealed class LoanEditViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly Loan _loan;

        private Employee _selectedEmployee;
        private LoanTypeOption _selectedType;
        private string _principal;
        private string _installment;
        private int _startYear;
        private int _startMonth;
        private string _reason;
        private string _scheduleText = string.Empty;

        public LoanEditViewModel(AppServices services, IReadOnlyList<Employee> employees, Loan existing)
        {
            _services = services;
            _loan = existing ?? new Loan();

            foreach (Employee employee in employees) Employees.Add(employee);
            foreach (LoanType type in Enum.GetValues(typeof(LoanType))) Types.Add(new LoanTypeOption(type));
            for (int y = DateTime.Today.Year - 2; y <= DateTime.Today.Year + 2; y++) Years.Add(y);
            for (int m = 1; m <= 12; m++) Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));

            if (existing != null)
            {
                _selectedEmployee = FindEmployee(existing.EmployeeId);
                _selectedType = FindType(existing.Type);
                _principal = existing.Principal.ToString(CultureInfo.InvariantCulture);
                _installment = existing.MonthlyInstallment.ToString(CultureInfo.InvariantCulture);
                _startYear = existing.StartYear;
                _startMonth = existing.StartMonth;
                _reason = existing.Reason;
                Title = "Modifier le prêt";
            }
            else
            {
                _selectedEmployee = Employees.Count > 0 ? Employees[0] : null;
                _selectedType = FindType(LoanType.Loan);
                _startYear = DateTime.Today.Year;
                _startMonth = DateTime.Today.Month;
                Title = "Nouveau prêt / avance";
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            RecomputeSchedule();
        }

        public Action<bool> RequestClose { get; set; }

        public string Title { get; }

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<LoanTypeOption> Types { get; } = new ObservableCollection<LoanTypeOption>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();

        public bool CanChooseEmployee => _loan.Id == 0;

        public Employee SelectedEmployee { get => _selectedEmployee; set => Set(ref _selectedEmployee, value); }
        public LoanTypeOption SelectedType { get => _selectedType; set => Set(ref _selectedType, value); }

        public string Principal
        {
            get => _principal;
            set { if (Set(ref _principal, value)) RecomputeSchedule(); }
        }

        public string MonthlyInstallment
        {
            get => _installment;
            set { if (Set(ref _installment, value)) RecomputeSchedule(); }
        }

        public int StartYear { get => _startYear; set => Set(ref _startYear, value); }
        public int StartMonth { get => _startMonth; set => Set(ref _startMonth, value); }
        public string Reason { get => _reason; set => Set(ref _reason, value); }

        public string ScheduleText { get => _scheduleText; private set => Set(ref _scheduleText, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void RecomputeSchedule()
        {
            if (TryDecimal(_principal, out decimal principal) && TryDecimal(_installment, out decimal installment)
                && principal > 0m && installment > 0m)
            {
                int months = (int)Math.Ceiling(principal / installment);
                ScheduleText = months + " mensualité(s) — dernière : " +
                    (principal - installment * (months - 1)).ToString("N2", Fr) + " DA";
            }
            else
            {
                ScheduleText = string.Empty;
            }
        }

        private void Save()
        {
            if (_selectedEmployee == null)
            {
                Dialogs.Error("Sélectionnez un employé.");
                return;
            }

            if (!TryDecimal(_principal, out decimal principal))
            {
                Dialogs.Error("Montant du prêt invalide.");
                return;
            }

            if (!TryDecimal(_installment, out decimal installment))
            {
                Dialogs.Error("Mensualité invalide.");
                return;
            }

            _loan.EmployeeId = _selectedEmployee.Id;
            _loan.Type = _selectedType != null ? _selectedType.Value : LoanType.Loan;
            _loan.Principal = principal;
            _loan.MonthlyInstallment = installment;
            _loan.StartYear = _startYear;
            _loan.StartMonth = _startMonth;
            _loan.Reason = _reason;

            Result<long> result = _services.Loans.Save(_loan);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }

        private static bool TryDecimal(string text, out decimal value)
        {
            return decimal.TryParse((text ?? string.Empty).Replace(" ", "").Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private Employee FindEmployee(long id)
        {
            foreach (Employee employee in Employees)
            {
                if (employee.Id == id) return employee;
            }

            return null;
        }

        private LoanTypeOption FindType(LoanType type)
        {
            foreach (LoanTypeOption option in Types)
            {
                if (option.Value == type) return option;
            }

            return null;
        }
    }
}
