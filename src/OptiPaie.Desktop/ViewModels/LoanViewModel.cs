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
    /// <summary>One loan as shown in the list, with its derived position.</summary>
    public sealed class LoanRowViewModel
    {
        public LoanRowViewModel(Loan loan, LoanSummary summary, string employeeName)
        {
            Loan = loan;
            Summary = summary;
            EmployeeName = employeeName;
        }

        public Loan Loan { get; }
        public LoanSummary Summary { get; }
        public long Id => Loan.Id;
        public string EmployeeName { get; }
        public string TypeLabel => LoanLabels.Type(Loan.Type);
        public string StatusLabel => LoanLabels.Status(Loan.Status);
        public string PrincipalText => Money(Summary.Principal);
        public string InstallmentText => Money(Summary.MonthlyInstallment);
        public string OutstandingText => Money(Summary.Outstanding);
        public string StartText => Loan.StartMonth.ToString("00", CultureInfo.InvariantCulture) + "/" + Loan.StartYear;
        public string RemainingText => Summary.RemainingInstallments + " mois";
        public bool IsActive => Loan.Status == LoanStatus.Active;
        public bool IsSuspended => Loan.Status == LoanStatus.Suspended;

        private static string Money(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
    }

    /// <summary>French labels for the loan enums (kept in one place).</summary>
    public static class LoanLabels
    {
        public static string Type(LoanType type) => type == LoanType.Advance ? "Avance sur salaire" : "Prêt";

        public static string Status(LoanStatus status)
        {
            switch (status)
            {
                case LoanStatus.Active: return "En cours";
                case LoanStatus.Settled: return "Soldé";
                case LoanStatus.Suspended: return "Suspendu";
                case LoanStatus.Cancelled: return "Annulé";
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// Prêts et avances — loans of a company with their outstanding balances. The
    /// monthly instalment of an active loan is added automatically to the employee's
    /// payslip and recovered when the payroll is saved.
    /// </summary>
    public sealed class LoanViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly Dictionary<long, string> _employeeNames = new Dictionary<long, string>();

        private Company _selectedCompany;
        private LoanRowViewModel _selectedLoan;
        private string _totalOutstandingText = "0";
        private string _activeCountText = "0";
        private string _statusMessage = string.Empty;

        public LoanViewModel(AppServices services)
        {
            _services = services;

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selectedLoan != null);
            DetailCommand = new RelayCommand(OpenDetail, () => _selectedLoan != null);
            SuspendCommand = new RelayCommand(Suspend, () => _selectedLoan != null && _selectedLoan.IsActive);
            ResumeCommand = new RelayCommand(Resume, () => _selectedLoan != null && _selectedLoan.IsSuspended);
            CancelCommand = new RelayCommand(CancelLoan, () => _selectedLoan != null && _selectedLoan.Loan.Status != LoanStatus.Cancelled);
            DeleteCommand = new RelayCommand(Delete, () => _selectedLoan != null);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<LoanRowViewModel> Loans { get; } = new ObservableCollection<LoanRowViewModel>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Load(); }
        }

        public LoanRowViewModel SelectedLoan
        {
            get => _selectedLoan;
            set => Set(ref _selectedLoan, value);
        }

        public string TotalOutstandingText { get => _totalOutstandingText; private set => Set(ref _totalOutstandingText, value); }
        public string ActiveCountText { get => _activeCountText; private set => Set(ref _activeCountText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DetailCommand { get; }
        public ICommand SuspendCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand CancelCommand { get; }
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
            Loans.Clear();
            _employeeNames.Clear();

            if (_selectedCompany == null)
            {
                TotalOutstandingText = ActiveCountText = "0";
                return;
            }

            foreach (Employee employee in _services.Employees.GetByCompany(_selectedCompany.Id))
            {
                _employeeNames[employee.Id] = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
            }

            IReadOnlyList<LoanSummary> summaries = _services.Loans.GetByCompany(_selectedCompany.Id);
            decimal totalOutstanding = 0m;
            int active = 0;

            foreach (LoanSummary summary in summaries)
            {
                Loan loan = _services.Loans.Get(summary.LoanId);
                if (loan == null) continue;

                _employeeNames.TryGetValue(loan.EmployeeId, out string name);
                Loans.Add(new LoanRowViewModel(loan, summary, name ?? "—"));

                if (loan.Status == LoanStatus.Active)
                {
                    active++;
                    totalOutstanding += summary.Outstanding;
                }
            }

            SelectedLoan = Loans.FirstOrDefault();
            TotalOutstandingText = totalOutstanding.ToString("N2", Fr);
            ActiveCountText = active.ToString();
            StatusMessage = Loans.Count + " prêt(s) · " + active + " en cours";
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

            ShowEditor(new LoanEditViewModel(_services, employees, null));
        }

        private void Edit()
        {
            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            ShowEditor(new LoanEditViewModel(_services, employees, _selectedLoan.Loan));
        }

        private void ShowEditor(LoanEditViewModel vm)
        {
            var window = new LoanEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                Load();
                StatusMessage = "Prêt enregistré.";
            }
        }

        private void OpenDetail()
        {
            var vm = new LoanDetailViewModel(_services, _selectedLoan.Id, _selectedLoan.EmployeeName);
            var window = new LoanDetailWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();

            // Manual repayments in the detail window can change the balance.
            Load();
        }

        private void Suspend() => Run(_services.Loans.SetStatus(_selectedLoan.Id, LoanStatus.Suspended), "Prêt suspendu.");
        private void Resume() => Run(_services.Loans.SetStatus(_selectedLoan.Id, LoanStatus.Active), "Prêt réactivé.");

        private void CancelLoan()
        {
            if (!Dialogs.Confirm("Annuler ce prêt ? Il ne sera plus déduit des salaires."))
            {
                return;
            }

            Run(_services.Loans.SetStatus(_selectedLoan.Id, LoanStatus.Cancelled), "Prêt annulé.");
        }

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer définitivement ce prêt et son historique de remboursement ?"))
            {
                return;
            }

            Run(_services.Loans.Delete(_selectedLoan.Id), "Prêt supprimé.");
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
