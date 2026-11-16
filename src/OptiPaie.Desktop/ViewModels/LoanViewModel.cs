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
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One loan as shown in the list, with its derived position.</summary>
    public sealed class LoanRowViewModel
    {
        private readonly HashSet<int> _repaidPeriods;

        public LoanRowViewModel(Loan loan, LoanSummary summary, string employeeName, HashSet<int> repaidPeriods = null)
        {
            Loan = loan;
            Summary = summary;
            EmployeeName = employeeName;
            _repaidPeriods = repaidPeriods ?? new HashSet<int>();
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
        public string RemainingText => Summary.RemainingInstallments + " " + OptiPaie.Desktop.Localization.TranslationSource.Instance["Common_MonthsSuffix"];
        public bool IsActive => Loan.Status == LoanStatus.Active;
        public bool IsSuspended => Loan.Status == LoanStatus.Suspended;

        /// <summary>True when an instalment was skipped on an already-issued payroll month.</summary>
        public bool IsLate => Summary.IsLate;

        /// <summary>Warning badge text ("⚠ 2") when late, empty otherwise.</summary>
        public string LateText => Summary.IsLate ? "⚠ " + Summary.MissedInstallments.ToString(CultureInfo.InvariantCulture) : string.Empty;

        /// <summary>True when this loan was live in the given packed period (start reached, still carried a balance or was repaid then).</summary>
        public bool LiveInPeriod(int period) =>
            ((Loan.StartYear * 12) + (Loan.StartMonth - 1)) <= period &&
            (Summary.Outstanding > 0m || _repaidPeriods.Contains(period));

        /// <summary>Semantic colour bucket for the status pill (shared with the other lists).</summary>
        public string StatusKind
        {
            get
            {
                switch (Loan.Status)
                {
                    case LoanStatus.Active: return "accent";      // repaying
                    case LoanStatus.Settled: return "success";    // paid off
                    case LoanStatus.Suspended: return "pending";  // paused
                    default: return "neutral";                    // cancelled
                }
            }
        }

        private static string Money(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
    }

    /// <summary>Localized labels for the loan enums (resolved for the active language).</summary>
    public static class LoanLabels
    {
        private static string L(string key) => OptiPaie.Desktop.Localization.TranslationSource.Instance[key];

        public static string Type(LoanType type) => L(type == LoanType.Advance ? "Enum_LoanType_Advance" : "Enum_LoanType_Loan");

        public static string Status(LoanStatus status)
        {
            switch (status)
            {
                case LoanStatus.Active: return L("Enum_LoanStatus_Active");
                case LoanStatus.Settled: return L("Enum_LoanStatus_Settled");
                case LoanStatus.Suspended: return L("Enum_LoanStatus_Suspended");
                case LoanStatus.Cancelled: return L("Enum_LoanStatus_Cancelled");
                default: return string.Empty;
            }
        }
    }

    /// <summary>A loan-list state filter (All / Running / Finished / Late / Suspended).</summary>
    public sealed class LoanStateOption
    {
        public LoanStateOption(string key, string label) { Key = key; Label = label; }
        public string Key { get; }
        public string Label { get; }
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

        /// <summary>The role may create/edit/delete loans (hides New/Edit/Delete buttons).</summary>
        public bool CanManage => OptiPaie.Desktop.Composition.ScreenAccess.CanEdit(_services, "loans");
        private readonly Dictionary<long, string> _employeeNames = new Dictionary<long, string>();

        private Company _selectedCompany;
        private LoanRowViewModel _selectedLoan;
        private string _totalOutstandingText = "0";
        private string _activeCountText = "0";
        private string _lateCountText = "0";
        private string _statusMessage = string.Empty;

        private readonly List<LoanRowViewModel> _allRows = new List<LoanRowViewModel>();
        private LoanStateOption _selectedState;
        private EmployeeFilterOption _selectedEmployeeFilter;
        private MonthOption _selectedMonth;
        private int _selectedYear = DateTime.Today.Year;

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
            ReportCommand = new RelayCommand(Report, () => _selectedCompany != null);

            BuildStateFilters();
            BuildMonthFilters();
            BuildYears();
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<LoanRowViewModel> Loans { get; } = new ObservableCollection<LoanRowViewModel>();

        public ObservableCollection<LoanStateOption> StateFilters { get; } = new ObservableCollection<LoanStateOption>();
        public ObservableCollection<EmployeeFilterOption> EmployeeFilters { get; } = new ObservableCollection<EmployeeFilterOption>();
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();

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
        public string LateCountText { get => _lateCountText; private set => Set(ref _lateCountText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public LoanStateOption SelectedState
        {
            get => _selectedState;
            set { if (Set(ref _selectedState, value)) ApplyFilters(); }
        }

        public EmployeeFilterOption SelectedEmployeeFilter
        {
            get => _selectedEmployeeFilter;
            set { if (Set(ref _selectedEmployeeFilter, value)) ApplyFilters(); }
        }

        public MonthOption SelectedMonth
        {
            get => _selectedMonth;
            set { if (Set(ref _selectedMonth, value)) ApplyFilters(); }
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set { if (Set(ref _selectedYear, value)) ApplyFilters(); }
        }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DetailCommand { get; }
        public ICommand SuspendCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ReportCommand { get; }

        private static string L(string key) => OptiPaie.Desktop.Localization.TranslationSource.Instance[key];

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
            _allRows.Clear();
            _employeeNames.Clear();

            if (_selectedCompany == null)
            {
                TotalOutstandingText = ActiveCountText = LateCountText = "0";
                RebuildEmployeeFilter();
                return;
            }

            foreach (Employee employee in _services.Employees.GetByCompany(_selectedCompany.Id))
            {
                _employeeNames[employee.Id] = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
            }

            IReadOnlyList<LoanSummary> summaries = _services.Loans.GetByCompany(_selectedCompany.Id);

            foreach (LoanSummary summary in summaries)
            {
                Loan loan = _services.Loans.Get(summary.LoanId);
                if (loan == null) continue;

                var repaidPeriods = new HashSet<int>();
                foreach (LoanRepayment r in _services.Loans.GetRepayments(loan.Id))
                {
                    repaidPeriods.Add((r.Year * 12) + (r.Month - 1));
                }

                _employeeNames.TryGetValue(loan.EmployeeId, out string name);
                _allRows.Add(new LoanRowViewModel(loan, summary, name ?? "—", repaidPeriods));
            }

            RebuildEmployeeFilter();
            ApplyFilters();
        }

        /// <summary>Applies the state / employee / month filters to <see cref="_allRows"/> and refreshes the KPIs.</summary>
        private void ApplyFilters()
        {
            Loans.Clear();

            string state = _selectedState?.Key ?? "all";
            long empId = _selectedEmployeeFilter?.Id ?? 0;
            int monthValue = _selectedMonth?.Value ?? 0;
            int period = monthValue > 0 ? (_selectedYear * 12) + (monthValue - 1) : 0;

            foreach (LoanRowViewModel row in _allRows)
            {
                if (empId > 0 && row.Loan.EmployeeId != empId) continue;
                if (!MatchesState(row, state)) continue;
                if (period > 0 && !row.LiveInPeriod(period)) continue;
                Loans.Add(row);
            }

            SelectedLoan = Loans.FirstOrDefault();

            decimal totalOutstanding = _allRows.Where(r => r.Loan.Status == LoanStatus.Active).Sum(r => r.Summary.Outstanding);
            int active = _allRows.Count(r => r.Loan.Status == LoanStatus.Active);
            int late = _allRows.Count(r => r.IsLate);

            TotalOutstandingText = totalOutstanding.ToString("N2", Fr);
            ActiveCountText = active.ToString();
            LateCountText = late.ToString();
            StatusMessage = string.Format(L("Loan_CountStatus"), Loans.Count, active);
        }

        private static bool MatchesState(LoanRowViewModel row, string state)
        {
            switch (state)
            {
                case "running": return row.Loan.Status == LoanStatus.Active;
                case "finished": return row.Loan.Status == LoanStatus.Settled;
                case "suspended": return row.Loan.Status == LoanStatus.Suspended;
                case "late": return row.IsLate;
                default: return true; // "all"
            }
        }

        private void BuildStateFilters()
        {
            StateFilters.Add(new LoanStateOption("all", L("Common_All")));
            StateFilters.Add(new LoanStateOption("running", L("Enum_LoanStatus_Active")));
            StateFilters.Add(new LoanStateOption("finished", L("Enum_LoanStatus_Settled")));
            StateFilters.Add(new LoanStateOption("late", L("Loan_State_Late")));
            StateFilters.Add(new LoanStateOption("suspended", L("Enum_LoanStatus_Suspended")));
            _selectedState = StateFilters[0];
        }

        private void BuildMonthFilters()
        {
            Months.Add(new MonthOption(0, L("Common_All")));
            for (int m = 1; m <= 12; m++)
            {
                Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));
            }
            _selectedMonth = Months[0];
        }

        private void BuildYears()
        {
            int current = DateTime.Today.Year;
            for (int y = current + 1; y >= current - 6; y--) Years.Add(y);
            _selectedYear = current;
        }

        private void RebuildEmployeeFilter()
        {
            EmployeeFilters.Clear();
            EmployeeFilters.Add(new EmployeeFilterOption(0, L("Common_All")));
            foreach (var pair in _allRows
                         .Select(r => new { r.Loan.EmployeeId, r.EmployeeName })
                         .GroupBy(x => x.EmployeeId)
                         .Select(g => g.First())
                         .OrderBy(x => x.EmployeeName, StringComparer.CurrentCultureIgnoreCase))
            {
                EmployeeFilters.Add(new EmployeeFilterOption(pair.EmployeeId, pair.EmployeeName ?? "—"));
            }

            _selectedEmployeeFilter = EmployeeFilters[0];
            Raise(nameof(SelectedEmployeeFilter));
        }

        private void Report()
        {
            if (_selectedCompany == null) return;

            long? employeeId = _selectedEmployeeFilter != null && _selectedEmployeeFilter.Id > 0
                ? _selectedEmployeeFilter.Id
                : (long?)null;

            int month = _selectedMonth?.Value ?? 0;
            int fromMonth = month > 0 ? month : 1;
            int toMonth = month > 0 ? month : 12;

            LoanLedgerReport report = _services.Loans.BuildLedger(
                _selectedCompany.Id, employeeId, _selectedYear, fromMonth, _selectedYear, toMonth);

            if (report.Lines.Count == 0)
            {
                Dialogs.Info(L("Loan_Report_Empty"));
                return;
            }

            SavePdf(
                p => QuestPDF.Fluent.Document.Create(new Documents.LoanLedgerDocument(report).Compose).GeneratePdf(p),
                "Etat_prets_" + _selectedYear);
        }

        private void SavePdf(Action<string> generate, string defaultName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Document PDF (*.pdf)|*.pdf", FileName = defaultName + ".pdf" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                generate(dialog.FileName);
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch { Dialogs.Info(L("Common_FileSaved") + Environment.NewLine + dialog.FileName); }
            }
            catch (Exception ex)
            {
                _services.Logger?.Error("Rapport prêts", ex);
                Dialogs.ErrorWithLog("Impossible de générer le PDF. تعذّر إنشاء ملف PDF.", ex);
            }
        }

        private void New()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info(L("Common_SelectCompanyFirst"));
                return;
            }

            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            if (employees.Count == 0)
            {
                Dialogs.Info(L("Loan_NoActiveEmployees"));
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
                StatusMessage = L("Loan_SavedMsg");
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

        private void Suspend() => Run(_services.Loans.SetStatus(_selectedLoan.Id, LoanStatus.Suspended), L("Loan_SuspendedMsg"));
        private void Resume() => Run(_services.Loans.SetStatus(_selectedLoan.Id, LoanStatus.Active), L("Loan_ResumedMsg"));

        private void CancelLoan()
        {
            if (!Dialogs.Confirm(L("Loan_ConfirmCancel")))
            {
                return;
            }

            Run(_services.Loans.SetStatus(_selectedLoan.Id, LoanStatus.Cancelled), L("Loan_CancelledMsg"));
        }

        private void Delete()
        {
            if (!Dialogs.Confirm(L("Loan_ConfirmDelete")))
            {
                return;
            }

            Run(_services.Loans.Delete(_selectedLoan.Id), L("Loan_DeletedMsg"));
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
