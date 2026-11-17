using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.ViewModels.Attendance;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The three attendance reports, reachable in one click from the attendance screen and generated
    /// as print-ready PDFs with the same look as the payroll reports:
    ///   1 the company monthly sheet (employees × days, one letter per day, with a legend);
    ///   2 the per-employee monthly summary (days due / worked / absent / leave / half / late / deduction);
    ///   3 the whole-company period totals.
    /// The figures come from the SAME attendance records and the SAME pre-run review payroll uses.
    /// </summary>
    public sealed class AttendanceReportsViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private int _selectedYear;
        private int _selectedMonth;

        public AttendanceReportsViewModel(AppServices services, int year, int month)
        {
            _services = services;
            _selectedYear = year;
            _selectedMonth = month;

            for (int m = 1; m <= 12; m++) Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));
            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);

            SheetCommand = new RelayCommand(ExportSheet);
            SummaryCommand = new RelayCommand(ExportSummary);
            PeriodCommand = new RelayCommand(ExportPeriod);
        }

        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();

        public int SelectedYear { get => _selectedYear; set => Set(ref _selectedYear, value); }
        public int SelectedMonth { get => _selectedMonth; set => Set(ref _selectedMonth, value); }

        public ICommand SheetCommand { get; }
        public ICommand SummaryCommand { get; }
        public ICommand PeriodCommand { get; }

        public Action RequestClose { get; set; }

        private string PeriodLabel => Fr.DateTimeFormat.GetMonthName(_selectedMonth) + " " + _selectedYear;
        private static string L(string k) => OptiPaie.Desktop.Localization.TranslationSource.Instance[k];

        // ---- 1 · company sheet ----------------------------------------------

        private void ExportSheet()
        {
            AttendanceSheetModel model = BuildSheetModel();
            if (model == null) return;
            Save(p => Document.Create(new AttendanceSheetDocument(model).Compose).GeneratePdf(p),
                "Feuille_presence_" + _selectedYear + "_" + _selectedMonth.ToString("00"));
        }

        /// <summary>Builds the company monthly sheet model (public so a render harness can reuse it).</summary>
        public AttendanceSheetModel BuildSheetModel()
        {
            Company company = _services.CompanyContext.Active;
            if (company == null) return null;

            int days = DateTime.DaysInMonth(_selectedYear, _selectedMonth);
            CompanyWorkCalendar cal = _services.WorkCalendar?.GetSettings(company.Id);
            var holidays = _services.Holidays?.GetForYear(company.Id, _selectedYear)
                ?.Select(h => h.HolidayDate.Date).ToHashSet() ?? new HashSet<DateTime>();

            var records = _services.Attendance.GetCompanyMonth(company.Id, _selectedYear, _selectedMonth)
                .GroupBy(r => r.EmployeeId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.WorkDate.Day, r => r.Status));

            var model = new AttendanceSheetModel
            {
                CompanyName = company.NameFr,
                PeriodLabel = PeriodLabel,
                Days = days,
                // French legend: the PDF engine (QuestPDF 2022.12) cannot shape Arabic, so the printed
                // sheet is French — like every other payroll document in the product.
                Legend = new List<(string, string)>
                {
                    ("P", "Présent"),
                    ("A", "Absent"),
                    ("J", "Absence justifiée"),
                    ("C", "Congé"),
                    ("½", "Demi-journée"),
                    ("R", "Retard"),
                    ("M", "Mission"),
                    ("T", "Repos travaillé"),
                }
            };

            foreach (Employee e in _services.Employees.GetByCompany(company.Id)
                         .OrderBy(e => (e.LastNameFr + " " + e.FirstNameFr).Trim(), StringComparer.CurrentCultureIgnoreCase))
            {
                records.TryGetValue(e.Id, out Dictionary<int, AttendanceStatus> byDay);
                var cells = new string[days];
                for (int d = 1; d <= days; d++)
                {
                    var date = new DateTime(_selectedYear, _selectedMonth, d);
                    bool rest = (cal != null && !cal.IsWorkDay(date.DayOfWeek)) || holidays.Contains(date.Date);
                    if (byDay != null && byDay.TryGetValue(d, out AttendanceStatus s))
                        cells[d - 1] = AttendanceAppearance.Letter(s, false);
                    else
                        cells[d - 1] = rest ? string.Empty : "P"; // present by default on a work day
                }
                model.Rows.Add(new AttendanceSheetRow { Name = (e.LastNameFr + " " + e.FirstNameFr).Trim(), Day = cells });
            }

            return model;
        }

        // ---- 2 & 3 · summary + period totals --------------------------------

        /// <summary>Builds the per-employee/period summary model (public so a render harness can reuse it).</summary>
        public AttendanceSummaryModel BuildSummaryModel()
        {
            Company company = _services.CompanyContext.Active;
            var model = new AttendanceSummaryModel { CompanyName = company?.NameFr, PeriodLabel = PeriodLabel };
            if (company == null) return model;

            var late = _services.Attendance.GetCompanyMonthlySummary(company.Id, _selectedYear, _selectedMonth)
                .GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.First().LateCount);

            foreach (PayrollReviewRow r in _services.Batch.BuildReview(company.Id, _selectedYear, _selectedMonth))
            {
                late.TryGetValue(r.EmployeeId, out int lateCount);
                model.Rows.Add(new AttendanceSummaryRow
                {
                    Name = r.Employee,
                    DaysDue = r.DaysDue,
                    Worked = r.Worked,
                    Absent = r.Absent,
                    Leave = r.Leave,
                    Half = r.Half,
                    Late = lateCount,
                    Deduction = r.Deduction
                });
            }

            model.Totals = new AttendanceSummaryRow
            {
                DaysDue = model.Rows.Sum(x => x.DaysDue),
                Worked = model.Rows.Sum(x => x.Worked),
                Absent = model.Rows.Sum(x => x.Absent),
                Leave = model.Rows.Sum(x => x.Leave),
                Half = model.Rows.Sum(x => x.Half),
                Late = model.Rows.Sum(x => x.Late),
                Deduction = model.Rows.Sum(x => x.Deduction)
            };
            return model;
        }

        private void ExportSummary()
        {
            AttendanceSummaryModel m = BuildSummaryModel();
            Save(p => Document.Create(new AttendanceSummaryDocument(m).Compose).GeneratePdf(p),
                "Recap_presence_" + _selectedYear + "_" + _selectedMonth.ToString("00"));
        }

        private void ExportPeriod()
        {
            AttendanceSummaryModel m = BuildSummaryModel();
            Save(p => Document.Create(new AttendancePeriodSummaryDocument(m).Compose).GeneratePdf(p),
                "Synthese_presence_" + _selectedYear + "_" + _selectedMonth.ToString("00"));
        }

        private void Save(Action<string> generate, string defaultName)
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
                _services.Logger?.Error("Rapport présence", ex);
                Dialogs.ErrorWithLog("Impossible de générer le PDF. تعذّر إنشاء ملف PDF.", ex);
            }
        }
    }
}
