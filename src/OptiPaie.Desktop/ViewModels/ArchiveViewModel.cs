using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>A single archived payslip row.</summary>
    public sealed class ArchiveRow
    {
        public long PayslipId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string PeriodText { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string NetText { get; set; }
    }

    /// <summary>Archive module: search archived payslips and reprint the Fiche de Paie.</summary>
    public sealed class ArchiveViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly FicheService _fiche = new FicheService();

        private Company _selectedCompany;
        private int _selectedYear = DateTime.Now.Year;
        private int _selectedMonth;
        private ArchiveRow _selectedRow;

        public ArchiveViewModel(AppServices services)
        {
            _services = services;

            Companies = new ObservableCollection<Company>();
            Rows = new ObservableCollection<ArchiveRow>();
            Years = BuildYears();
            Months = BuildMonths();

            SearchCommand = new RelayCommand(Search);
            PreviewCommand = new RelayCommand(() => WithFiche(m => _fiche.Preview(m)));
            PrintCommand = new RelayCommand(() => WithFiche(m => _fiche.Print(m)));
            CorrectCommand = new RelayCommand(Correct);
            DeleteCommand = new RelayCommand(Delete);
            HistoryCommand = new RelayCommand(OpenHistory);
        }

        public ObservableCollection<Company> Companies { get; }
        public ObservableCollection<ArchiveRow> Rows { get; }
        public List<int> Years { get; }
        public List<EnumOption> Months { get; }

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set => Set(ref _selectedCompany, value);
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set { if (Set(ref _selectedYear, value)) Search(); }
        }

        public int SelectedMonth
        {
            get => _selectedMonth;
            set { if (Set(ref _selectedMonth, value)) Search(); }
        }

        public ArchiveRow SelectedRow
        {
            get => _selectedRow;
            set { if (Set(ref _selectedRow, value)) Raise(nameof(HasSelection)); }
        }

        public bool HasSelection => _selectedRow != null;

        public bool IsEmpty => Rows.Count == 0;

        public ICommand SearchCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand PrintCommand { get; }
        public ICommand CorrectCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand HistoryCommand { get; }

        public void OnActivated()
        {
            // The active company comes from the single global selector in the header.
            _selectedCompany = _services.CompanyContext.Active;
            Raise(nameof(SelectedCompany));
            Search();
        }

        private void Search()
        {
            Rows.Clear();

            if (SelectedCompany != null)
            {
                int? month = _selectedMonth == 0 ? (int?)null : _selectedMonth;

                foreach (PayrollRun run in _services.Archive.SearchRuns(SelectedCompany.Id, _selectedYear, month))
                {
                    PayrollRun full = _services.Archive.GetRun(run.Id);
                    if (full == null)
                    {
                        continue;
                    }

                    foreach (Payslip ps in full.Payslips)
                    {
                        Employee e = _services.Employees.Get(ps.EmployeeId);
                        string name = e == null
                            ? ("Employé #" + ps.EmployeeId)
                            : (e.LastNameFr + " " + e.FirstNameFr).Trim();

                        Rows.Add(new ArchiveRow
                        {
                            PayslipId = ps.Id,
                            EmployeeId = ps.EmployeeId,
                            EmployeeName = name,
                            Year = full.PeriodYear,
                            Month = full.PeriodMonth,
                            PeriodText = MonthName(full.PeriodMonth) + " " + full.PeriodYear,
                            NetText = ps.NetSalaire.ToString("N2", Fr) + " DA"
                        });
                    }
                }
            }

            SelectedRow = Rows.FirstOrDefault();
            Raise(nameof(IsEmpty));
        }

        private void WithFiche(Action<FichePaieModel> action)
        {
            if (SelectedRow == null)
            {
                return;
            }

            Payslip payslip = _services.Archive.GetPayslip(SelectedRow.PayslipId);
            if (payslip == null)
            {
                Dialogs.Error("Ce bulletin est introuvable.");
                return;
            }

            Employee employee = _services.Employees.Get(SelectedRow.EmployeeId);
            Company company = _services.Companies.Get(SelectedCompany.Id);

            FichePaieModel model = _fiche.FromPayslip(
                company, employee, payslip, _services.Localization.IsRightToLeft,
                SelectedRow.Year, SelectedRow.Month, L("Fiche_RectificatifMark"));

            try
            {
                action(model);
            }
            catch (Exception ex)
            {
                Dialogs.ErrorWithLog("Impossible de produire la fiche de paie. تعذّر إنشاء كشف الراتب.", ex);
            }
        }

        private void Correct()
        {
            if (SelectedRow == null || SelectedCompany == null)
            {
                return;
            }

            Payslip payslip = _services.Archive.GetPayslip(SelectedRow.PayslipId);
            Employee employee = _services.Employees.Get(SelectedRow.EmployeeId);
            Company company = _services.Companies.Get(SelectedCompany.Id);
            if (payslip == null || employee == null || company == null)
            {
                Dialogs.Error(L("Archive_PreviewRow"));
                return;
            }

            var vm = new PayslipCorrectionViewModel(_services, payslip, company, employee, SelectedRow.Year, SelectedRow.Month);
            if (Dialogs.ShowPayslipCorrection(vm))
            {
                Search(); // the corrected version replaces the row (same employee/period)
            }
        }

        private void Delete()
        {
            if (SelectedRow == null || SelectedCompany == null)
            {
                return;
            }

            Payslip payslip = _services.Archive.GetPayslip(SelectedRow.PayslipId);
            if (payslip == null)
            {
                Dialogs.Error(L("Archive_PreviewRow"));
                return;
            }

            // The confirmation shows the REAL figures and escalates when a CNAS declaration exists.
            decimal loanReversal = payslip.Details
                .Where(d => d.ElementType == OptiPaie.Core.Enums.ElementType.Deduction &&
                            string.Equals(d.LabelFr, "Remboursement prêt", StringComparison.Ordinal))
                .Sum(d => d.Amount);

            string message = string.Format(L("Delete_Summary"), SelectedRow.EmployeeName, SelectedRow.PeriodText,
                                 payslip.NetSalaire.ToString("N2", Fr) + " DA")
                + Environment.NewLine + Environment.NewLine + L("Delete_Body");
            if (loanReversal > 0m)
            {
                message += Environment.NewLine + Environment.NewLine
                    + string.Format(L("Delete_LoanReversal"), loanReversal.ToString("N2", Fr) + " DA");
            }
            if (DeclarationExists(SelectedCompany.Id, SelectedRow.Year))
            {
                message += Environment.NewLine + Environment.NewLine
                    + string.Format(L("Delete_DeclarationWarning"), SelectedRow.PeriodText);
            }

            if (!Dialogs.Confirm(message))
            {
                return;
            }

            string reason = Dialogs.Prompt(L("Delete_Title"), L("Delete_Reason"), null, required: true);
            if (string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            Result result = _services.PayslipRevision.Cancel(SelectedRow.PayslipId, reason);
            if (!result.IsSuccess)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Dialogs.Info(L("Delete_Done"));
            Search();
        }

        private void OpenHistory()
        {
            if (SelectedCompany == null)
            {
                return;
            }

            int? month = _selectedMonth == 0 ? (int?)null : _selectedMonth;
            Dialogs.ShowPayslipHistory(new PayslipHistoryViewModel(_services, SelectedCompany.Id, _selectedYear, month));
            Search(); // a restore re-activates a payslip; refresh the active list
        }

        private bool DeclarationExists(long companyId, int year)
        {
            string token = " | " + year.ToString(CultureInfo.InvariantCulture) + " | ";
            string das = _services.Settings.Get("Cnas.Das.Log." + companyId, string.Empty);
            string dac = _services.Settings.Get("Cnas.Dac.Log." + companyId, string.Empty);
            return das.IndexOf(token, StringComparison.Ordinal) >= 0
                || dac.IndexOf(token, StringComparison.Ordinal) >= 0;
        }

        private string L(string key) => _services.Localization.GetString(key);

        private static string MonthName(int m)
        {
            if (m < 1 || m > 12)
            {
                return m.ToString("00", CultureInfo.InvariantCulture);
            }

            string name = Fr.DateTimeFormat.GetMonthName(m);
            return char.ToUpper(name[0], Fr) + name.Substring(1);
        }

        private static List<int> BuildYears()
        {
            int now = DateTime.Now.Year;
            var list = new List<int>();
            for (int y = now - 4; y <= now + 1; y++)
            {
                list.Add(y);
            }

            return list;
        }

        private static List<EnumOption> BuildMonths()
        {
            var list = new List<EnumOption> { new EnumOption(0, "Tous les mois") };
            for (int m = 1; m <= 12; m++)
            {
                string name = Fr.DateTimeFormat.GetMonthName(m);
                name = char.ToUpper(name[0], Fr) + name.Substring(1);
                list.Add(new EnumOption(m, name));
            }

            return list;
        }
    }
}
