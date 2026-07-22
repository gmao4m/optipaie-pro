using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The guided payroll flow: choose company → employee → period, the worksheet loads
    /// automatically, totals recompute live, and the Fiche de Paie is one click away.
    /// The engine and its services are used unchanged.
    /// </summary>
    public sealed class PayrollViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly FicheService _fiche = new FicheService();

        private Company _selectedCompany;
        private Employee _selectedEmployee;
        private int _selectedMonth = DateTime.Now.Month;
        private int _selectedYear = DateTime.Now.Year;
        private PayrollElement _selectedCatalog;
        private string _status = string.Empty;
        private string _attendanceNote;
        private bool _recomputing;
        private bool _loading;

        private PayrollResult _lastResult;
        private PayrollGenerationRequest _lastRequest;

        public PayrollViewModel(AppServices services)
        {
            _services = services;

            Companies = new ObservableCollection<Company>();
            Employees = new ObservableCollection<Employee>();
            Catalog = new ObservableCollection<PayrollElement>();
            Lines = new ObservableCollection<PayrollLineVM>();

            AddElementCommand = new RelayCommand(AddElement);
            RemoveLineCommand = new RelayCommand(p => RemoveLine(p as PayrollLineVM));
            SaveCommand = new RelayCommand(Save);
            PreviewCommand = new RelayCommand(() => WithFiche(m => _fiche.Preview(m)));
            PrintCommand = new RelayCommand(() => WithFiche(m => _fiche.Print(m)));
            ExportPdfCommand = new RelayCommand(ExportPdf);
            ResetCommand = new RelayCommand(LoadWorksheet);
            ManageItemsCommand = new RelayCommand(ManageItems);
        }

        public ObservableCollection<Company> Companies { get; }
        public ObservableCollection<Employee> Employees { get; }
        public ObservableCollection<PayrollElement> Catalog { get; }
        public ObservableCollection<PayrollLineVM> Lines { get; }

        /// <summary>
        /// What the Attendance module contributed to this calculation, or null when the
        /// module is locked or the month has no pointage. Shown in the worksheet so the
        /// figures are never a black box.
        /// </summary>
        public string AttendanceNote
        {
            get => _attendanceNote;
            private set => Set(ref _attendanceNote, value);
        }

        public List<EnumOption> Months { get; } = BuildMonths();
        public List<int> Years { get; } = BuildYears();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) ReloadEmployees(); }
        }

        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set { if (Set(ref _selectedEmployee, value)) LoadWorksheet(); }
        }

        public int SelectedMonth
        {
            get => _selectedMonth;
            set { if (Set(ref _selectedMonth, value)) Recompute(); }
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set { if (Set(ref _selectedYear, value)) Recompute(); }
        }

        public PayrollElement SelectedCatalogElement
        {
            get => _selectedCatalog;
            set => Set(ref _selectedCatalog, value);
        }

        public bool HasEmployee => _selectedEmployee != null;

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        // Totals (formatted for display).
        private PayrollTotals _totals = new PayrollTotals(0, 0, 0, 0, 0, 0, 0, 0, 0);
        private decimal _totalGains;
        private decimal _totalRetenues;
        private decimal _lissage;

        public string GrossText => Money(_totals.SalaireBrut);
        public string CotisableText => Money(_totals.BaseCotisable);
        public string CnasText => Money(_totals.CnasEmployee);
        public string ImposableText => Money(_totals.BaseImposable);
        public string AbattementText => Money(_totals.Abattement);
        public string IrgText => Money(_totals.Irg);
        public string NetText => Money(_totals.NetSalaire);
        public string TotalGainsText => Money(_totalGains);
        public string TotalRetenuesText => Money(_totalRetenues);
        public string LissageText => Money(_lissage);
        public bool HasLissage => _lissage != 0m;

        public ICommand AddElementCommand { get; }
        public ICommand RemoveLineCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand PrintCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ManageItemsCommand { get; }

        public void OnActivated()
        {
            Catalog.Clear();
            foreach (PayrollElement el in _services.PayrollElements.GetAll(false))
            {
                Catalog.Add(el);
            }

            // The active company comes from the single global selector in the header
            // (its setter reloads this company's employees — payroll logic is unchanged).
            SelectedCompany = _services.CompanyContext.Active;
        }

        private void ReloadEmployees()
        {
            Employees.Clear();
            if (SelectedCompany != null)
            {
                foreach (Employee e in _services.Employees.GetByCompany(SelectedCompany.Id, false))
                {
                    Employees.Add(e);
                }
            }

            SelectedEmployee = Employees.FirstOrDefault();
        }

        private void LoadWorksheet()
        {
            _loading = true;
            Lines.Clear();
            Raise(nameof(HasEmployee));

            if (SelectedEmployee == null)
            {
                _loading = false;
                _lastResult = null;
                UpdateTotals(new PayrollTotals(0, 0, 0, 0, 0, 0, 0, 0, 0));
                Status = "Sélectionnez un employé.";
                return;
            }

            Lines.Add(new PayrollLineVM(Recompute)
            {
                IsBaseSalary = true,
                IsGain = true,
                Rubrique = "Salaire de base",
                Base = SelectedEmployee.BaseSalary
            });

            foreach (EmployeeElement assignment in _services.Employees.GetElements(SelectedEmployee.Id))
            {
                if (!assignment.IsActive)
                {
                    continue;
                }

                PayrollElement el = _services.PayrollElements.Get(assignment.ElementId);
                if (el == null || el.IsDeleted || !el.IsEnabled)
                {
                    continue;
                }

                Lines.Add(new PayrollLineVM(Recompute)
                {
                    ElementId = el.Id,
                    IsGain = el.ElementType == ElementType.Gain,
                    Rubrique = el.NameFr,
                    Base = assignment.Amount ?? el.DefaultAmount ?? 0m
                });
            }

            // Loans integration — automatic, input only. When the Loans module is
            // licensed and the employee has an instalment due this period, it appears as
            // a deduction line. Removing the line before saving skips the recovery for
            // the month; the loan schedule itself is only touched when payroll is saved.
            if (_services.LicenseGate.IsEnabled(ModuleKeys.Loans))
            {
                decimal loanDue = _services.Loans.GetMonthlyDeduction(SelectedEmployee.Id, SelectedYear, SelectedMonth);
                if (loanDue > 0m)
                {
                    Lines.Add(new PayrollLineVM(Recompute)
                    {
                        IsManual = true,
                        IsLoan = true,
                        IsGain = false,
                        Rubrique = "Remboursement prêt",
                        Base = loanDue
                    });
                }
            }

            _loading = false;
            Recompute();
        }

        private void AddElement()
        {
            if (SelectedEmployee == null || SelectedCatalogElement == null)
            {
                return;
            }

            PayrollElement el = SelectedCatalogElement;
            Lines.Add(new PayrollLineVM(Recompute)
            {
                ElementId = el.Id,
                IsGain = el.ElementType == ElementType.Gain,
                Rubrique = el.NameFr,
                Base = el.DefaultAmount ?? 0m
            });

            Recompute();
        }

        private void ManageItems()
        {
            Dialogs.ShowPayrollItemsManager(new PayrollItemsViewModel(_services));

            PayrollElement keep = SelectedCatalogElement;
            Catalog.Clear();
            foreach (PayrollElement el in _services.PayrollElements.GetAll(false))
            {
                Catalog.Add(el);
            }

            if (keep != null)
            {
                foreach (PayrollElement el in Catalog)
                {
                    if (el.Id == keep.Id)
                    {
                        SelectedCatalogElement = el;
                        break;
                    }
                }
            }
        }

        private void RemoveLine(PayrollLineVM line)
        {
            if (line == null || line.IsBaseSalary)
            {
                return;
            }

            Lines.Remove(line);
            Recompute();
        }

        private void Recompute()
        {
            if (_loading || _recomputing || SelectedEmployee == null || SelectedCompany == null)
            {
                return;
            }

            _recomputing = true;
            try
            {
                _lastRequest = BuildRequest();
                _lastResult = _services.Payroll.Preview(_lastRequest);

                if (_lastResult == null || !_lastResult.IsSuccess)
                {
                    PayrollMessage error = _lastResult?.Errors.FirstOrDefault();
                    Status = error != null ? error.Text : "Le calcul a échoué.";
                    return;
                }

                UpdateTotals(_lastResult.Totals);
                Status = "Calcul à jour";
            }
            finally
            {
                _recomputing = false;
            }
        }

        private PayrollGenerationRequest BuildRequest()
        {
            decimal? baseOverride = null;
            var elements = new List<PayrollElementEntry>();

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

            decimal monthDays = DateTime.DaysInMonth(SelectedYear, SelectedMonth);
            decimal workedDays = monthDays;
            decimal workedHours = 0m;

            // Attendance integration — automatic, no import/export. When the Attendance
            // module is licensed AND the period has recorded days, absences reduce the
            // worked days and the recorded hours feed hour-based elements. Without
            // records the behaviour is byte-for-byte the previous one. This only changes
            // the ENGINE INPUTS: no formula, rate or legal rule is touched.
            AttendanceNote = null;
            if (_services.LicenseGate.IsEnabled(ModuleKeys.Attendance))
            {
                AttendanceSummary summary =
                    _services.Attendance.GetMonthlySummary(SelectedEmployee.Id, SelectedYear, SelectedMonth);
                if (summary != null && summary.RecordedDays > 0)
                {
                    workedDays = Math.Max(0m, monthDays - summary.AbsentDays);
                    workedHours = summary.WorkedHours;
                    AttendanceNote = "Présence : " + summary.AbsentDays + " absence(s) · "
                        + summary.WorkedHours.ToString("0.##", Fr) + " h travaillées · "
                        + summary.OvertimeHours.ToString("0.##", Fr) + " h supplémentaires";
                }
            }

            return new PayrollGenerationRequest
            {
                CompanyId = SelectedCompany.Id,
                EmployeeId = SelectedEmployee.Id,
                Year = SelectedYear,
                Month = SelectedMonth,
                WorkedDays = workedDays,
                WorkableDays = monthDays,
                WorkedHours = workedHours,
                BaseSalaryOverride = baseOverride,
                Elements = elements
            };
        }

        private void UpdateTotals(PayrollTotals totals)
        {
            _totals = totals;

            decimal gains = 0m, deductions = 0m;
            foreach (PayrollLineVM line in Lines)
            {
                if (line.Gain.HasValue) gains += line.Gain.Value;
                if (line.Retenue.HasValue) deductions += line.Retenue.Value;
            }

            _totalGains = gains;
            _totalRetenues = deductions + totals.CnasEmployee + totals.Irg;

            _lissage = 0m;
            if (_lastResult != null)
            {
                foreach (PayrollCalculationStep step in _lastResult.Trace)
                {
                    if (step.Key == "LISSAGE")
                    {
                        _lissage = step.Amount;
                    }
                }
            }

            Raise(nameof(GrossText));
            Raise(nameof(CotisableText));
            Raise(nameof(CnasText));
            Raise(nameof(ImposableText));
            Raise(nameof(AbattementText));
            Raise(nameof(IrgText));
            Raise(nameof(NetText));
            Raise(nameof(TotalGainsText));
            Raise(nameof(TotalRetenuesText));
            Raise(nameof(LissageText));
            Raise(nameof(HasLissage));
        }

        private void Save()
        {
            if (!EnsureComputed())
            {
                return;
            }

            Result<long> result = _services.Payroll.Generate(_lastRequest);
            if (!result.IsSuccess)
            {
                Dialogs.Error(result.Error);
                return;
            }

            // The payslip is archived — now, and only now, record the loan recovery so
            // the instalment shown on this payslip is deducted from the balance exactly
            // once. Recording is idempotent per period, so re-saving is safe. The payroll
            // engine is untouched: this runs entirely in the Loans module.
            string loanNote = RecordLoanRecovery();

            Status = "Paie enregistrée";
            Dialogs.Info("La paie a été enregistrée dans l'archive." + loanNote);
        }

        /// <summary>
        /// Records this period's loan recovery when the worksheet still carries the
        /// automatic "Remboursement prêt" line. Returns a short note for the dialog, or
        /// an empty string when nothing was recorded.
        /// </summary>
        private string RecordLoanRecovery()
        {
            if (!_services.LicenseGate.IsEnabled(ModuleKeys.Loans)) return string.Empty;
            if (!Lines.Any(l => l.IsLoan)) return string.Empty;

            Result<decimal> recorded =
                _services.Loans.RecordPayrollDeductions(SelectedEmployee.Id, SelectedYear, SelectedMonth);

            if (recorded.IsFailure || recorded.Value <= 0m) return string.Empty;

            return Environment.NewLine + "Remboursement de prêt enregistré : " +
                   recorded.Value.ToString("N2", Fr) + " DA.";
        }

        private void ExportPdf()
        {
            WithFiche(model =>
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = "fiche_paie_" + SelectedMonth.ToString("00", Fr) + "_" + SelectedYear + ".pdf"
                };

                if (dialog.ShowDialog() == true)
                {
                    _fiche.ExportPdf(model, dialog.FileName);
                    Dialogs.Info("Le PDF a été enregistré.");
                }
            });
        }

        private void WithFiche(Action<FichePaieModel> action)
        {
            if (!EnsureComputed())
            {
                return;
            }

            // Build the model straight from the worksheet so N/Base and Taux on the
            // fiche always match what the accountant sees (never blank); the statutory
            // totals come from the engine result.
            PayrollTotals t = _lastResult.Totals;
            var model = new FichePaieModel
            {
                Company = SelectedCompany,
                Employee = SelectedEmployee,
                Year = SelectedYear,
                Month = SelectedMonth,
                IsArabic = _services.Localization.IsRightToLeft,
                SalaireBrut = t.SalaireBrut,
                BaseCotisable = t.BaseCotisable,
                CnasEmployee = t.CnasEmployee,
                BaseImposable = t.BaseImposable,
                IrgBrut = t.IrgBrut,
                Abattement = t.Abattement,
                Irg = t.Irg,
                NetSalaire = t.NetSalaire,
                WorkedDays = _lastRequest.WorkedDays
            };

            foreach (PayrollLineVM line in Lines)
            {
                model.Lines.Add(new FicheLineModel
                {
                    Label = line.Rubrique,
                    BaseText = (line.Base ?? 0m).ToString("N2", Fr),
                    TauxText = string.IsNullOrWhiteSpace(line.Taux) ? string.Empty : line.Taux.Trim(),
                    Gain = line.IsGain ? line.Amount : (decimal?)null,
                    Retenue = line.IsGain ? (decimal?)null : line.Amount
                });
            }

            try
            {
                action(model);
            }
            catch (Exception ex)
            {
                Dialogs.Error("Impossible de produire la fiche de paie :\r\n" + ex.Message);
            }
        }

        private bool EnsureComputed()
        {
            if (SelectedEmployee == null)
            {
                Dialogs.Error("Sélectionnez un employé.");
                return false;
            }

            if (_lastResult == null || !_lastResult.IsSuccess)
            {
                Recompute();
            }

            return _lastResult != null && _lastResult.IsSuccess;
        }

        private static string Money(decimal v) => v.ToString("N2", Fr) + " DA";

        private static List<EnumOption> BuildMonths()
        {
            var list = new List<EnumOption>();
            for (int m = 1; m <= 12; m++)
            {
                string name = Fr.DateTimeFormat.GetMonthName(m);
                name = char.ToUpper(name[0], Fr) + name.Substring(1);
                list.Add(new EnumOption(m, name));
            }

            return list;
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
    }
}
