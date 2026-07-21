using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One repayment row in the loan detail.</summary>
    public sealed class RepaymentRowViewModel
    {
        public RepaymentRowViewModel(LoanRepayment repayment)
        {
            Repayment = repayment;
        }

        public LoanRepayment Repayment { get; }
        public long Id => Repayment.Id;
        public string Period => Repayment.Month.ToString("00", CultureInfo.InvariantCulture) + "/" + Repayment.Year;
        public string AmountText => Repayment.Amount.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
        public string Source => Repayment.IsManual ? "Manuel" : "Paie";
    }

    /// <summary>
    /// Loan detail: the derived balance plus the full repayment ledger. Repayments can
    /// be added by hand (e.g. a cash reimbursement) or removed to correct a mistake —
    /// the outstanding balance and the loan status follow automatically.
    /// </summary>
    public sealed class LoanDetailViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly long _loanId;

        private RepaymentRowViewModel _selectedRepayment;
        private string _principalText, _repaidText, _outstandingText, _installmentText, _statusText;
        private string _manualAmount;
        private int _manualYear;
        private int _manualMonth;
        private string _statusMessage = string.Empty;

        public LoanDetailViewModel(AppServices services, long loanId, string employeeName)
        {
            _services = services;
            _loanId = loanId;
            EmployeeName = employeeName;

            for (int y = DateTime.Today.Year - 3; y <= DateTime.Today.Year + 1; y++) Years.Add(y);
            for (int m = 1; m <= 12; m++) Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));
            _manualYear = DateTime.Today.Year;
            _manualMonth = DateTime.Today.Month;

            AddRepaymentCommand = new RelayCommand(AddRepayment);
            RemoveRepaymentCommand = new RelayCommand(RemoveRepayment, () => _selectedRepayment != null);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        public Action RequestClose { get; set; }

        public string EmployeeName { get; }

        public ObservableCollection<RepaymentRowViewModel> Repayments { get; } = new ObservableCollection<RepaymentRowViewModel>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();

        public RepaymentRowViewModel SelectedRepayment
        {
            get => _selectedRepayment;
            set => Set(ref _selectedRepayment, value);
        }

        public string PrincipalText { get => _principalText; private set => Set(ref _principalText, value); }
        public string RepaidText { get => _repaidText; private set => Set(ref _repaidText, value); }
        public string OutstandingText { get => _outstandingText; private set => Set(ref _outstandingText, value); }
        public string InstallmentText { get => _installmentText; private set => Set(ref _installmentText, value); }
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public string ManualAmount { get => _manualAmount; set => Set(ref _manualAmount, value); }
        public int ManualYear { get => _manualYear; set => Set(ref _manualYear, value); }
        public int ManualMonth { get => _manualMonth; set => Set(ref _manualMonth, value); }

        public ICommand AddRepaymentCommand { get; }
        public ICommand RemoveRepaymentCommand { get; }
        public ICommand CloseCommand { get; }

        private void Load()
        {
            LoanSummary summary = _services.Loans.GetSummary(_loanId);
            Loan loan = _services.Loans.Get(_loanId);
            if (summary == null || loan == null)
            {
                return;
            }

            PrincipalText = summary.Principal.ToString("N2", Fr);
            RepaidText = summary.Repaid.ToString("N2", Fr);
            OutstandingText = summary.Outstanding.ToString("N2", Fr);
            InstallmentText = summary.MonthlyInstallment.ToString("N2", Fr);
            StatusText = LoanLabels.Status(loan.Status);

            Repayments.Clear();
            foreach (LoanRepayment repayment in _services.Loans.GetRepayments(_loanId))
            {
                Repayments.Add(new RepaymentRowViewModel(repayment));
            }

            StatusMessage = summary.RemainingInstallments + " mensualité(s) restante(s)";
        }

        private void AddRepayment()
        {
            if (!decimal.TryParse((_manualAmount ?? string.Empty).Replace(" ", "").Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount))
            {
                Dialogs.Error("Montant invalide.");
                return;
            }

            Result result = _services.Loans.AddManualRepayment(_loanId, _manualYear, _manualMonth, amount);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            ManualAmount = string.Empty;
            Load();
            StatusMessage = "Remboursement ajouté.";
        }

        private void RemoveRepayment()
        {
            if (!Dialogs.Confirm("Supprimer ce remboursement ?"))
            {
                return;
            }

            Result result = _services.Loans.RemoveRepayment(_selectedRepayment.Id);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Load();
            StatusMessage = "Remboursement supprimé.";
        }
    }
}
