using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Monthly attendance synthesis for one company: the totals payroll consumes,
    /// exportable to PDF or CSV.
    /// </summary>
    public sealed class AttendanceMonthViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = new CultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly Company _company;

        private int _year;
        private int _month;
        private string _statusMessage = string.Empty;

        public AttendanceMonthViewModel(AppServices services, Company company, int year, int month)
        {
            _services = services;
            _company = company;
            _year = year;
            _month = month;

            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);
            for (int m = 1; m <= 12; m++) Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));

            ExportPdfCommand = new RelayCommand(ExportPdf, () => Rows.Count > 0);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => Rows.Count > 0);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        /// <summary>Set by the host window so the view model can close it.</summary>
        public Action RequestClose { get; set; }

        public ObservableCollection<AttendanceSummary> Rows { get; } = new ObservableCollection<AttendanceSummary>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();

        public string CompanyName => _company.NameFr;
        public string PeriodLabel => Fr.DateTimeFormat.GetMonthName(_month) + " " + _year.ToString(CultureInfo.InvariantCulture);

        public int Year
        {
            get => _year;
            set { if (Set(ref _year, value)) { Raise(nameof(PeriodLabel)); Load(); } }
        }

        public int Month
        {
            get => _month;
            set { if (Set(ref _month, value)) { Raise(nameof(PeriodLabel)); Load(); } }
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand ExportPdfCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand CloseCommand { get; }

        private void Load()
        {
            Rows.Clear();
            IReadOnlyList<AttendanceSummary> summaries =
                _services.Attendance.GetCompanyMonthlySummary(_company.Id, _year, _month);
            foreach (AttendanceSummary s in summaries) Rows.Add(s);

            StatusMessage = Rows.Count == 0
                ? "Aucun pointage enregistré pour cette période."
                : Rows.Count + " employé(s) · " + Rows.Sum(r => r.OvertimeHours).ToString("0.##", CultureInfo.InvariantCulture)
                  + " h supplémentaires · " + Rows.Sum(r => r.AbsentDays) + " jour(s) d'absence";
        }

        private void ExportPdf()
        {
            string path = AskPath("Document PDF (*.pdf)|*.pdf", ".pdf");
            if (path == null) return;

            try
            {
                var model = new AttendanceReportModel
                {
                    CompanyName = _company.NameFr,
                    PeriodLabel = PeriodLabel,
                    Rows = Rows.ToList()
                };
                var document = new AttendanceReportDocument(model);
                Document.Create(document.Compose).GeneratePdf(path);
                Open(path);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export PDF présence", ex);
                Dialogs.Error("Impossible de générer le PDF : " + ex.Message);
            }
        }

        private void ExportCsv()
        {
            string path = AskPath("Fichier CSV (*.csv)|*.csv", ".csv");
            if (path == null) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Employé;Présents;Absents;Congés;Fériés;Repos;Retards;Minutes de retard;Heures;Heures supp.");
                foreach (AttendanceSummary r in Rows)
                {
                    sb.AppendLine(string.Join(";",
                        Escape(r.EmployeeName),
                        r.PresentDays.ToString(CultureInfo.InvariantCulture),
                        r.AbsentDays.ToString(CultureInfo.InvariantCulture),
                        r.LeaveDays.ToString(CultureInfo.InvariantCulture),
                        r.HolidayDays.ToString(CultureInfo.InvariantCulture),
                        r.RestDays.ToString(CultureInfo.InvariantCulture),
                        r.LateCount.ToString(CultureInfo.InvariantCulture),
                        r.LateMinutes.ToString(CultureInfo.InvariantCulture),
                        r.WorkedHours.ToString("0.##", CultureInfo.InvariantCulture),
                        r.OvertimeHours.ToString("0.##", CultureInfo.InvariantCulture)));
                }

                // UTF-8 with BOM so Excel opens the accents correctly.
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Open(path);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export CSV présence", ex);
                Dialogs.Error("Impossible de générer le fichier : " + ex.Message);
            }
        }

        private string AskPath(string filter, string extension)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                FileName = "Presence_" + Sanitize(_company.NameFr) + "_" +
                           _year.ToString("0000", CultureInfo.InvariantCulture) + "-" +
                           _month.ToString("00", CultureInfo.InvariantCulture) + extension
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void Open(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _services.Logger.Warn("Ouverture du fichier exporté impossible : " + ex.Message);
                Dialogs.Info("Fichier enregistré :" + Environment.NewLine + path);
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Entreprise";
            var sb = new StringBuilder();
            foreach (char c in value)
            {
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == ' ' ? '_' : c);
            }

            return sb.ToString();
        }
    }

    /// <summary>A month number with its French name.</summary>
    public sealed class MonthOption
    {
        public MonthOption(int value, string label) { Value = value; Label = label; }
        public int Value { get; }
        public string Label { get; }
    }
}
