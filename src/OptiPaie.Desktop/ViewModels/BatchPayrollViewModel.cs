using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Services;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One employee row in the pre-run plan.</summary>
    public sealed class BatchCheckRow
    {
        public string EmployeeName { get; set; }
        public string StatusLabel { get; set; }
        public string StatusKind { get; set; }   // success / pending / danger — drives the pill
        public string Reason { get; set; }
    }

    /// <summary>One employee row in the completed-run results.</summary>
    public sealed class BatchResultRow
    {
        public long PayslipId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string StatusLabel { get; set; }
        public string StatusKind { get; set; }
        public string Message { get; set; }
        public string NetText { get; set; }
        public bool CanOpen => PayslipId > 0;
    }

    /// <summary>
    /// "Traiter tout le mois" — drives the batch payroll orchestration for the active
    /// company and a chosen period. It owns no calculation: it calls
    /// <see cref="IBatchPayrollService"/>, which loops the existing per-employee path.
    /// </summary>
    public sealed class BatchPayrollViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly Core.Interfaces.Services.IBatchPayrollService _batch;
        private readonly FicheService _fiche = new FicheService();
        private readonly Company _company;

        private int _selectedYear = DateTime.Today.Year;
        private int _selectedMonth = DateTime.Today.Month;
        private bool _hasPlan;
        private bool _isRunning;
        private bool _hasResults;
        private int _progressDone;
        private int _progressTotal;
        private string _statusMessage = string.Empty;
        private BatchPayrollPlan _plan;
        private BatchPayrollResult _result;
        private BatchResultRow _selectedResult;

        public BatchPayrollViewModel(AppServices services)
        {
            _services = services;
            _company = services.CompanyContext.Active;

            _batch = new BatchPayrollService(
                services.Employees, services.PayrollElements, services.Loans, services.Attendance,
                services.Payroll, services.Contracts, services.Archive, services.LicenseGate.IsEnabled);

            for (int y = DateTime.Today.Year - 3; y <= DateTime.Today.Year + 1; y++) Years.Add(y);
            for (int m = 1; m <= 12; m++) Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));

            CheckCommand = new RelayCommand(Check, () => _company != null && !_isRunning);
            RunCommand = new RelayCommand(async () => await RunAsync(), () => CanRun);
            OpenSelectedCommand = new RelayCommand(OpenSelected, () => _selectedResult != null && _selectedResult.CanOpen);
            ExportAllCommand = new RelayCommand(ExportAll, () => _hasResults && _result != null && _result.Succeeded > 0);

            if (_company != null) Check();
        }

        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();
        public ObservableCollection<BatchCheckRow> PlanRows { get; } = new ObservableCollection<BatchCheckRow>();
        public ObservableCollection<BatchResultRow> ResultRows { get; } = new ObservableCollection<BatchResultRow>();

        public string CompanyName => _company != null ? _company.NameFr : "—";

        public int SelectedYear
        {
            get => _selectedYear;
            set { if (Set(ref _selectedYear, value)) Check(); }
        }

        public int SelectedMonth
        {
            get => _selectedMonth;
            set { if (Set(ref _selectedMonth, value)) Check(); }
        }

        public BatchResultRow SelectedResult
        {
            get => _selectedResult;
            set => Set(ref _selectedResult, value);
        }

        // -- plan summary -----------------------------------------------------
        public bool HasPlan { get => _hasPlan; private set => Set(ref _hasPlan, value); }
        public string TotalText => _plan != null ? _plan.TotalActive.ToString() : "0";
        public string ReadyText => _plan != null ? _plan.Ready.ToString() : "0";
        public string BlockedText => _plan != null ? _plan.Blocked.ToString() : "0";
        public string WarningsText => _plan != null ? _plan.Warnings.ToString() : "0";
        public bool AlreadyArchived => _plan != null && _plan.AlreadyArchived;

        // -- progress ---------------------------------------------------------
        public bool IsRunning { get => _isRunning; private set { if (Set(ref _isRunning, value)) Raise(nameof(CanRun)); } }
        public int ProgressDone { get => _progressDone; private set { if (Set(ref _progressDone, value)) Raise(nameof(ProgressText)); } }
        public int ProgressTotal { get => _progressTotal; private set { if (Set(ref _progressTotal, value)) Raise(nameof(ProgressText)); } }
        public string ProgressText => ProgressDone + " / " + ProgressTotal;

        // -- results ----------------------------------------------------------
        public bool HasResults { get => _hasResults; private set => Set(ref _hasResults, value); }
        public string SucceededText => _result != null ? _result.Succeeded.ToString() : "0";
        public string SkippedText => _result != null ? _result.Skipped.ToString() : "0";
        public string FailedText => _result != null ? _result.Failed.ToString() : "0";
        public bool IsComplete => _result != null && _result.IsComplete;

        public bool CanRun => _company != null && !_isRunning && _plan != null && _plan.Ready > 0 && !_plan.AlreadyArchived;

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand CheckCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand OpenSelectedCommand { get; }
        public ICommand ExportAllCommand { get; }

        public Action RequestClose { get; set; }

        // -- pre-run check ----------------------------------------------------
        private void Check()
        {
            if (_company == null) return;

            HasResults = false;
            ResultRows.Clear();
            _result = null;

            _plan = _batch.Plan(_company.Id, _selectedYear, _selectedMonth);
            PlanRows.Clear();
            foreach (BatchEmployeeCheck c in _plan.Employees)
            {
                PlanRows.Add(new BatchCheckRow
                {
                    EmployeeName = c.EmployeeName,
                    StatusLabel = SeverityLabel(c.Severity),
                    StatusKind = SeverityKind(c.Severity),
                    Reason = c.Reason
                });
            }

            HasPlan = true;
            StatusMessage = _plan.AlreadyArchived
                ? L("Batch_AlreadyArchived")
                : string.Format(Fr, "{0} employé(s) · {1} prêt(s) · {2} bloqué(s)", _plan.TotalActive, _plan.Ready, _plan.Blocked);

            RaiseSummary();
        }

        // -- run --------------------------------------------------------------
        private async Task RunAsync()
        {
            if (!CanRun) return;

            IsRunning = true;
            HasResults = false;
            ProgressTotal = _plan.TotalActive;
            ProgressDone = 0;
            StatusMessage = L("Batch_Processing");

            long companyId = _company.Id;
            int year = _selectedYear, month = _selectedMonth;

            var progress = new Progress<BatchProgress>(p =>
            {
                ProgressTotal = p.Total;
                ProgressDone = p.Done;
                if (!string.IsNullOrEmpty(p.CurrentEmployee)) StatusMessage = L("Batch_Processing") + " " + p.CurrentEmployee;
            });

            BatchPayrollResult result;
            try
            {
                result = await Task.Run(() => _batch.Run(companyId, year, month, progress));
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Batch payroll run", ex);
                Dialogs.Error("Le traitement a échoué : " + ex.Message);
                IsRunning = false;
                return;
            }

            _result = result;
            ResultRows.Clear();
            foreach (BatchEmployeeResult r in result.Results)
            {
                ResultRows.Add(new BatchResultRow
                {
                    PayslipId = r.PayslipId,
                    EmployeeId = r.EmployeeId,
                    EmployeeName = r.EmployeeName,
                    StatusLabel = OutcomeLabel(r.Outcome),
                    StatusKind = OutcomeKind(r.Outcome),
                    Message = r.Message,
                    NetText = r.Outcome == BatchOutcome.Succeeded ? r.Net.ToString("N2", Fr) + " DA" : "—"
                });
            }

            IsRunning = false;
            HasResults = true;
            StatusMessage = string.Format(Fr, "{0} réussi(s) · {1} ignoré(s) · {2} échoué(s)",
                result.Succeeded, result.Skipped, result.Failed);
            RaiseResults();
        }

        // -- drill-in ---------------------------------------------------------
        private void OpenSelected()
        {
            if (_selectedResult == null || !_selectedResult.CanOpen) return;

            Payslip payslip = _services.Archive.GetPayslip(_selectedResult.PayslipId);
            if (payslip == null) { Dialogs.Error("Ce bulletin est introuvable."); return; }

            Employee employee = _services.Employees.Get(_selectedResult.EmployeeId);
            FichePaieModel model = _fiche.FromPayslip(
                _company, employee, payslip, _services.Localization.IsRightToLeft, _selectedYear, _selectedMonth);

            try { _fiche.Preview(model); }
            catch (Exception ex) { Dialogs.Error("Impossible d'ouvrir la fiche : " + ex.Message); }
        }

        // -- batch export -----------------------------------------------------
        private void ExportAll()
        {
            if (_result == null || _result.Succeeded == 0) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = string.Format(Fr, "Paie_{0}_{1:0000}-{2:00}.pdf",
                    Slug(_company.NameFr), _selectedYear, _selectedMonth)
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var models = new List<FichePaieModel>();
                foreach (BatchResultRow row in ResultRows.Where(r => r.CanOpen))
                {
                    Payslip payslip = _services.Archive.GetPayslip(row.PayslipId);
                    if (payslip == null) continue;
                    Employee employee = _services.Employees.Get(row.EmployeeId);
                    models.Add(_fiche.FromPayslip(_company, employee, payslip, _services.Localization.IsRightToLeft, _selectedYear, _selectedMonth));
                }

                // One combined PDF: each payslip appends its own page(s) to the container.
                Document.Create(container =>
                {
                    foreach (FichePaieModel m in models) new FichePaieDocument(m).Compose(container);
                }).GeneratePdf(dialog.FileName);

                Dialogs.Info(models.Count + " bulletin(s) exporté(s) dans un seul PDF.");
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch { /* opening is best-effort */ }
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Batch payslip export", ex);
                Dialogs.Error("Impossible d'exporter les bulletins : " + ex.Message);
            }
        }

        // -- helpers ----------------------------------------------------------
        private void RaiseSummary()
        {
            Raise(nameof(TotalText)); Raise(nameof(ReadyText)); Raise(nameof(BlockedText));
            Raise(nameof(WarningsText)); Raise(nameof(AlreadyArchived)); Raise(nameof(CanRun));
        }

        private void RaiseResults()
        {
            Raise(nameof(SucceededText)); Raise(nameof(SkippedText)); Raise(nameof(FailedText)); Raise(nameof(IsComplete));
        }

        private static string SeverityKind(BatchCheckSeverity s) =>
            s == BatchCheckSeverity.Ok ? "success" : s == BatchCheckSeverity.Warning ? "pending" : "danger";

        private string SeverityLabel(BatchCheckSeverity s) =>
            s == BatchCheckSeverity.Ok ? L("Batch_Ready") : s == BatchCheckSeverity.Warning ? L("Batch_Warning") : L("Batch_Blocked");

        private static string OutcomeKind(BatchOutcome o) =>
            o == BatchOutcome.Succeeded ? "success" : o == BatchOutcome.Skipped ? "neutral" : "danger";

        private string OutcomeLabel(BatchOutcome o) =>
            o == BatchOutcome.Succeeded ? L("Batch_Succeeded") : o == BatchOutcome.Skipped ? L("Batch_Skipped") : L("Batch_Failed");

        private static string Slug(string s) =>
            string.IsNullOrWhiteSpace(s) ? "entreprise" : new string(s.Where(ch => char.IsLetterOrDigit(ch)).ToArray());

        private static string L(string key) => OptiPaie.Desktop.Localization.TranslationSource.Instance[key];
    }
}
