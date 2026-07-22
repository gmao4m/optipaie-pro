using System;
using System.Collections.ObjectModel;
using System.Data;
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
    /// The Reports Center: pick a report and a period, preview it, and export to PDF or
    /// Excel-friendly CSV. Every report shares one uniform table shape, so the preview,
    /// the PDF and the CSV all work the same way. Read-only.
    /// </summary>
    public sealed class ReportsViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;

        private Company _selectedCompany;
        private ReportDescriptor _selectedReport;
        private int _selectedYear = DateTime.Today.Year;
        private int _selectedMonth = DateTime.Today.Month;
        private DataView _preview;
        private string _title = string.Empty;
        private string _subtitle = string.Empty;
        private string _statusMessage = string.Empty;
        private ReportTable _current;

        public ReportsViewModel(AppServices services)
        {
            _services = services;

            foreach (ReportDescriptor r in _services.Reports.GetReports()) Reports.Add(r);
            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);
            for (int m = 1; m <= 12; m++) Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));
            _selectedReport = Reports.FirstOrDefault();

            GenerateCommand = new RelayCommand(Generate);
            ExportPdfCommand = new RelayCommand(ExportPdf, () => _current != null && _current.Rows.Count > 0);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => _current != null && _current.Rows.Count > 0);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<ReportDescriptor> Reports { get; } = new ObservableCollection<ReportDescriptor>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Generate(); }
        }

        public ReportDescriptor SelectedReport
        {
            get => _selectedReport;
            set { if (Set(ref _selectedReport, value)) { Raise(nameof(MonthEnabled)); Generate(); } }
        }

        public int SelectedYear { get => _selectedYear; set { if (Set(ref _selectedYear, value)) Generate(); } }
        public int SelectedMonth { get => _selectedMonth; set { if (Set(ref _selectedMonth, value)) Generate(); } }

        /// <summary>Month picker is only relevant for month-scoped reports.</summary>
        public bool MonthEnabled => _selectedReport != null && _selectedReport.NeedsMonth;

        public DataView Preview { get => _preview; private set => Set(ref _preview, value); }
        public string Title { get => _title; private set => Set(ref _title, value); }
        public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand GenerateCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand ExportCsvCommand { get; }

        public void OnActivated()
        {
            // The active company comes from the single global selector in the header.
            _selectedCompany = _services.CompanyContext.Active;
            Raise(nameof(SelectedCompany));
            Generate();
        }

        private void Generate()
        {
            if (_selectedCompany == null || _selectedReport == null)
            {
                Preview = null;
                _current = null;
                return;
            }

            ReportTable table = _services.Reports.Build(_selectedReport.Key, _selectedCompany.Id, _selectedYear, _selectedMonth);
            _current = table;
            Title = table.Title;
            Subtitle = table.Subtitle;
            Preview = ToDataView(table);
            StatusMessage = table.Rows.Count + " ligne(s)";
        }

        private static DataView ToDataView(ReportTable table)
        {
            var dt = new DataTable();
            var used = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (string col in table.Columns)
            {
                string name = col;
                int i = 2;
                while (!used.Add(name)) name = col + " " + i++;
                dt.Columns.Add(name, typeof(string));
            }

            foreach (var row in table.Rows)
            {
                dt.Rows.Add(row.ToArray());
            }

            return dt.DefaultView;
        }

        private void ExportPdf()
        {
            string path = AskPath("Document PDF (*.pdf)|*.pdf", ".pdf");
            if (path == null) return;

            try
            {
                var document = new ReportDocument(_current);
                Document.Create(document.Compose).GeneratePdf(path);
                Open(path);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export PDF rapport", ex);
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
                sb.AppendLine(string.Join(";", _current.Columns.Select(Escape)));
                foreach (var row in _current.Rows)
                {
                    sb.AppendLine(string.Join(";", row.Select(Escape)));
                }

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); // BOM for Excel accents
                Open(path);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export CSV rapport", ex);
                Dialogs.Error("Impossible de générer le fichier : " + ex.Message);
            }
        }

        private string AskPath(string filter, string extension)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                FileName = "Rapport_" + (_selectedReport != null ? _selectedReport.Key : "hr") + "_" +
                           _selectedYear.ToString("0000", CultureInfo.InvariantCulture) + extension
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                _services.Logger.Warn("Ouverture du fichier impossible : " + ex.Message);
                Dialogs.Info("Fichier enregistré :" + Environment.NewLine + path);
            }
        }

        private static string Escape(string v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            return v.Replace(';', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
