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
    /// Annual-leave balances of a whole company for a year — the "who still has how
    /// many days" view, exportable to PDF or CSV.
    /// </summary>
    public sealed class LeaveBalancesViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Company _company;

        private int _year;
        private string _statusMessage = string.Empty;

        public LeaveBalancesViewModel(AppServices services, Company company, int year)
        {
            _services = services;
            _company = company;
            _year = year;

            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);

            ExportPdfCommand = new RelayCommand(ExportPdf, () => Rows.Count > 0);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => Rows.Count > 0);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        public Action RequestClose { get; set; }

        public ObservableCollection<LeaveBalance> Rows { get; } = new ObservableCollection<LeaveBalance>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();

        public string CompanyName => _company.NameFr;

        public int Year
        {
            get => _year;
            set { if (Set(ref _year, value)) Load(); }
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand ExportPdfCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand CloseCommand { get; }

        private void Load()
        {
            Rows.Clear();
            IReadOnlyList<LeaveBalance> balances = _services.Leave.GetCompanyBalances(_company.Id, _year);
            foreach (LeaveBalance balance in balances) Rows.Add(balance);

            StatusMessage = Rows.Count == 0
                ? "Aucun employé dans cette entreprise."
                : Rows.Count + " employé(s) · " + Num(Rows.Sum(r => r.Remaining)) + " jour(s) de congé restants au total";
        }

        private void ExportPdf()
        {
            string path = AskPath("Document PDF (*.pdf)|*.pdf", ".pdf");
            if (path == null) return;

            try
            {
                var document = new LeaveBalanceReportDocument(new LeaveBalanceReportModel
                {
                    CompanyName = _company.NameFr,
                    Year = _year,
                    Rows = Rows.ToList()
                });

                Document.Create(document.Compose).GeneratePdf(path);
                Open(path);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export PDF soldes de congés", ex);
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
                sb.AppendLine("Employé;Droit annuel;Pris;En attente;Restant;Autres congés;Sans solde");
                foreach (LeaveBalance r in Rows)
                {
                    sb.AppendLine(string.Join(";",
                        Escape(r.EmployeeName),
                        Num(r.Entitlement), Num(r.Taken), Num(r.Pending),
                        Num(r.Remaining), Num(r.OtherLeaveDays), Num(r.UnpaidDays)));
                }

                // UTF-8 with BOM so Excel opens the accents correctly.
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Open(path);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export CSV soldes de congés", ex);
                Dialogs.Error("Impossible de générer le fichier : " + ex.Message);
            }
        }

        private string AskPath(string filter, string extension)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                FileName = "Conges_" + Sanitize(_company.NameFr) + "_" +
                           _year.ToString("0000", CultureInfo.InvariantCulture) + extension
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

        private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

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
}
