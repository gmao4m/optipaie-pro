using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Corrects an archived payslip. The grid is seeded from the payslip's own frozen lines — so it
    /// opens showing exactly what was paid — and every line is editable; the net recomputes live
    /// through the SAME engine (preview). On save, a mandatory reason is required and the atomic
    /// <see cref="Core.Interfaces.Services.IPayslipRevisionService.Correct"/> produces a new version
    /// that supersedes the original. No wizard, one screen.
    /// </summary>
    public sealed class PayslipCorrectionViewModel : ObservableObject
    {
        private const string BaseSalaryLabel = "Salaire de base";
        private const string LoanLineLabel = "Remboursement prêt";
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly Payslip _original;
        private readonly Company _company;
        private readonly Employee _employee;
        private readonly int _year;
        private readonly int _month;

        private bool _loading;
        private bool _recomputing;
        private string _reason = string.Empty;
        private string _status = string.Empty;
        private decimal _newNet;
        private PayrollGenerationRequest _lastRequest;

        public PayslipCorrectionViewModel(AppServices services, Payslip originalWithDetails, Company company, Employee employee, int year, int month)
        {
            _services = services;
            _original = originalWithDetails;
            _company = company;
            _employee = employee;
            _year = year;
            _month = month;

            Lines = new ObservableCollection<PayrollLineVM>();
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            LoadFromPayslip();
        }

        public ObservableCollection<PayrollLineVM> Lines { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>Set by the window; invoked with true after a successful correction.</summary>
        public Action<bool> RequestClose { get; set; }

        public string Title => L("Correct_Title");
        public string Subtitle => L("Correct_Subtitle");
        public string LinesHeader => L("Correct_Lines");
        public string ReasonLabel => L("Correct_Reason");
        public string SaveLabel => L("Correct_Save");
        public string EmployeeName => (_employee?.LastNameFr + " " + _employee?.FirstNameFr).Trim();
        public string PeriodText => MonthName(_month) + " " + _year;

        public string OldNetLabel => L("Correct_OldNet");
        public string NewNetLabel => L("Correct_NewNet");
        public string OldNetText => Money(_original.NetSalaire);
        public string NewNetText => Money(_newNet);

        public string Reason
        {
            get => _reason;
            set => Set(ref _reason, value);
        }

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>A sober warning shown only when a CNAS declaration already exists for the period.</summary>
        public bool HasDeclarationWarning { get; private set; }
        public string DeclarationWarning { get; private set; }

        private void LoadFromPayslip()
        {
            _loading = true;

            foreach (PayrollDetail d in _original.Details.OrderBy(d => d.DisplayOrder).ThenBy(d => d.Id))
            {
                bool isGain = d.ElementType == ElementType.Gain;
                bool isBase = d.ElementId == null && d.DisplayOrder == 0 && isGain;
                bool isManual = d.ElementId == null && !isBase;

                Lines.Add(new PayrollLineVM(Recompute)
                {
                    IsBaseSalary = isBase,
                    IsManual = isManual,
                    IsLoan = isManual && string.Equals(d.LabelFr, LoanLineLabel, StringComparison.Ordinal),
                    ElementId = d.ElementId ?? 0,
                    IsGain = isGain,
                    Rubrique = d.LabelFr,
                    Base = d.Amount.ToString("0.00", Fr)
                });
            }

            HasDeclarationWarning = DeclarationExists();
            DeclarationWarning = HasDeclarationWarning
                ? string.Format(L("Correct_DeclarationWarning"), PeriodText)
                : string.Empty;

            _loading = false;
            Recompute();
        }

        private void Recompute()
        {
            if (_loading || _recomputing)
            {
                return;
            }

            _recomputing = true;
            try
            {
                _lastRequest = BuildRequest();
                PayrollResult result = _services.Payroll.Preview(_lastRequest);
                if (result == null || !result.IsSuccess)
                {
                    PayrollMessage error = result?.Errors.FirstOrDefault();
                    Status = error != null ? error.Text : "Le calcul a échoué.";
                    return;
                }

                // Reflect the engine's exact per-line amounts, like the worksheet does.
                foreach (PayrollLineVM line in Lines)
                {
                    decimal? engine = null;
                    if (line.IsBaseSalary)
                    {
                        engine = result.Lines.FirstOrDefault(l => l.ElementId == null && l.DisplayOrder == 0 && l.ElementType == ElementType.Gain)?.Amount;
                    }
                    else if (!line.IsManual && line.ElementId > 0)
                    {
                        engine = result.Lines.FirstOrDefault(l => l.ElementId == line.ElementId)?.Amount;
                    }
                    line.SetEngineAmount(engine);
                }

                _newNet = result.Totals.NetSalaire;
                Raise(nameof(NewNetText));
                Status = string.Empty;
            }
            finally
            {
                _recomputing = false;
            }
        }

        private PayrollGenerationRequest BuildRequest()
        {
            // Mirrors PayrollViewModel.BuildRequest. Worked days are the full month: the frozen line
            // amounts are fed back as fixed inputs, so proration must not be applied a second time.
            decimal? baseOverride = null;
            var elements = new System.Collections.Generic.List<PayrollElementEntry>();

            foreach (PayrollLineVM line in Lines)
            {
                if (line.IsBaseSalary)
                {
                    baseOverride = line.Amount;
                    continue;
                }

                elements.Add(new PayrollElementEntry
                {
                    ElementId = line.IsManual ? 0 : line.ElementId,
                    LineAmount = line.Amount,
                    IsManual = line.IsManual,
                    ManualLabel = line.Rubrique,
                    ManualType = line.IsGain ? ElementType.Gain : ElementType.Deduction
                });
            }

            decimal monthDays = DateTime.DaysInMonth(_year, _month);
            return new PayrollGenerationRequest
            {
                CompanyId = _company.Id,
                EmployeeId = _employee.Id,
                Year = _year,
                Month = _month,
                WorkedDays = monthDays,
                WorkableDays = monthDays,
                WorkedHours = 0m,
                BaseSalaryOverride = baseOverride,
                Elements = elements
            };
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(Reason))
            {
                Dialogs.Error(L("Correct_Reason"));
                return;
            }

            if (_lastRequest == null)
            {
                Recompute();
                if (_lastRequest == null) return;
            }

            Result<long> result = _services.PayslipRevision.Correct(new PayslipCorrectionRequest
            {
                OriginalPayslipId = _original.Id,
                Amended = _lastRequest,
                Reason = Reason.Trim()
            });

            if (!result.IsSuccess)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Dialogs.Info(L("Correct_Done"));
            RequestClose?.Invoke(true);
        }

        private bool DeclarationExists()
        {
            // Matches the DAS (annual) and DAC traces recorded in settings: any line for this year.
            string token = " | " + _year.ToString(CultureInfo.InvariantCulture) + " | ";
            string das = _services.Settings.Get("Cnas.Das.Log." + _company.Id, string.Empty);
            string dac = _services.Settings.Get("Cnas.Dac.Log." + _company.Id, string.Empty);
            return das.IndexOf(token, StringComparison.Ordinal) >= 0
                || dac.IndexOf(token, StringComparison.Ordinal) >= 0;
        }

        private string L(string key) => _services.Localization.GetString(key);
        private static string Money(decimal v) => v.ToString("N2", Fr) + " DA";

        private string MonthName(int m)
        {
            if (m < 1 || m > 12) return m.ToString("00", CultureInfo.InvariantCulture);
            CultureInfo ci = _services.Localization.CurrentCulture;
            string name = ci.DateTimeFormat.GetMonthName(m);
            return string.IsNullOrEmpty(name) ? name : char.ToUpper(name[0], ci) + name.Substring(1);
        }
    }
}
