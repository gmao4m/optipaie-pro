using System;
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
    /// The per-company work calendar (Commit 2): the weekly work pattern, the half-day definition,
    /// the rest-day rate, and the proration basis — with a LIVE worked example (a real employee, 3
    /// absences, the current month) showing the deduction under each basis side by side. The default
    /// basis is today's behaviour; changing it affects FUTURE runs only (said on the screen).
    /// </summary>
    public sealed class WorkCalendarViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private readonly AppServices _services;
        private readonly long _companyId;
        private CompanyWorkCalendar _model;

        private ProrationBasis _basis;
        private string _restDayRate;
        private string _halfDayHours;
        private string _exampleText = string.Empty;

        public WorkCalendarViewModel(AppServices services)
        {
            _services = services;
            Company active = services.CompanyContext.Active;
            _companyId = active?.Id ?? 0;

            _model = _companyId > 0 ? services.WorkCalendar.GetSettings(_companyId) : new CompanyWorkCalendar();
            _basis = _model.ProrationBasis;
            _restDayRate = _model.RestDayRate.ToString("0.##", CultureInfo.InvariantCulture);
            _halfDayHours = _model.HalfDayHours.ToString("0.##", CultureInfo.InvariantCulture);

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            BuildExample();
        }

        /// <summary>Set by the host window: true = saved, false = cancelled.</summary>
        public Action<bool> RequestClose { get; set; }

        // Weekly pattern (work day = true). Algerian default Sun–Thu work, Fri–Sat rest.
        public bool WorkMon { get => _model.WorkMon; set { _model.WorkMon = value; Raise(nameof(WorkMon)); } }
        public bool WorkTue { get => _model.WorkTue; set { _model.WorkTue = value; Raise(nameof(WorkTue)); } }
        public bool WorkWed { get => _model.WorkWed; set { _model.WorkWed = value; Raise(nameof(WorkWed)); } }
        public bool WorkThu { get => _model.WorkThu; set { _model.WorkThu = value; Raise(nameof(WorkThu)); } }
        public bool WorkFri { get => _model.WorkFri; set { _model.WorkFri = value; Raise(nameof(WorkFri)); } }
        public bool WorkSat { get => _model.WorkSat; set { _model.WorkSat = value; Raise(nameof(WorkSat)); } }
        public bool WorkSun { get => _model.WorkSun; set { _model.WorkSun = value; Raise(nameof(WorkSun)); } }

        public string RestDayRate { get => _restDayRate; set => Set(ref _restDayRate, value); }
        public string HalfDayHours { get => _halfDayHours; set => Set(ref _halfDayHours, value); }

        // Proration basis — three honest, bilingual radio options.
        public bool IsMonthDays { get => _basis == ProrationBasis.MonthDays; set { if (value) SetBasis(ProrationBasis.MonthDays); } }
        public bool IsFixedThirtieths { get => _basis == ProrationBasis.FixedThirtieths; set { if (value) SetBasis(ProrationBasis.FixedThirtieths); } }
        public bool IsWorkingDays { get => _basis == ProrationBasis.WorkingDays; set { if (value) SetBasis(ProrationBasis.WorkingDays); } }

        public string MonthDaysLabel => "Jours du mois (÷ les jours réels du mois) — comportement actuel  ·  أيام الشهر (÷ الأيام الفعلية) — الحالي";
        public string FixedThirtiethsLabel => "Trentièmes fixes (÷ 30, toujours)  ·  أثلاث ثابتة (÷ 30 دائماً)";
        public string WorkingDaysLabel => "Jours ouvrables (÷ les jours travaillés du calendrier)  ·  أيام العمل (÷ الأيام القابلة للعمل)";

        public string FutureOnlyNote =>
            "Changer la base n'affecte que les paies FUTURES. Aucune paie déjà émise n'est recalculée." + Environment.NewLine +
            "تغيير الأساس يؤثّر فقط على الرواتب المستقبلية. لا يُعاد حساب أيّ راتب صادر.";

        public string ExampleText { get => _exampleText; private set => Set(ref _exampleText, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void SetBasis(ProrationBasis basis)
        {
            _basis = basis;
            Raise(nameof(IsMonthDays)); Raise(nameof(IsFixedThirtieths)); Raise(nameof(IsWorkingDays));
        }

        private void BuildExample()
        {
            if (_companyId <= 0) { ExampleText = string.Empty; return; }
            try
            {
                Employee emp = _services.Employees.GetByCompany(_companyId, false).FirstOrDefault(e => e.IsActive)
                               ?? _services.Employees.GetByCompany(_companyId, false).FirstOrDefault();
                if (emp == null) { ExampleText = "Aucun employé pour l'exemple.  ·  لا يوجد موظّف للمثال."; return; }

                DateTime today = DateTime.Today;
                ProrationExample ex = _services.WorkCalendar.BuildExample(emp.Id, today.Year, today.Month, 3);
                if (ex == null) { ExampleText = string.Empty; return; }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Exemple  ·  مثال : " + ex.EmployeeName + " — salaire " +
                    ex.BaseSalary.ToString("0.##", Fr) + " DA — 3 absences en " + Fr.DateTimeFormat.GetMonthName(ex.Month) + " " + ex.Year);
                foreach (ProrationExampleRow r in ex.Rows)
                {
                    sb.AppendLine("   " + BasisShort(r.Basis) + " : ÷ " + r.Denominator.ToString("0.##", Fr) +
                        "  →  retenue " + r.Deduction.ToString("0.##", Fr) + " DA" + (r.IsCurrentDefault ? "   (actuel)" : ""));
                }
                ExampleText = sb.ToString().TrimEnd();
            }
            catch (Exception)
            {
                ExampleText = string.Empty;
            }
        }

        private static string BasisShort(ProrationBasis b)
        {
            switch (b)
            {
                case ProrationBasis.FixedThirtieths: return "Trentièmes (÷30)  أثلاث";
                case ProrationBasis.WorkingDays: return "Jours ouvrables  أيام العمل";
                default: return "Jours du mois  أيام الشهر";
            }
        }

        private void Save()
        {
            if (_companyId <= 0) { Dialogs.Error("Sélectionnez une entreprise."); return; }
            if (!OptiPaie.Common.Text.FlexibleNumber.TryParse(_restDayRate, out decimal rate) || rate < 0m)
            {
                Dialogs.Error("Taux jour de repos invalide.  ·  معدّل يوم الراحة غير صالح."); return;
            }
            if (!OptiPaie.Common.Text.FlexibleNumber.TryParse(_halfDayHours, out decimal half) || half < 0m)
            {
                Dialogs.Error("Heures demi-journée invalides.  ·  ساعات نصف اليوم غير صالحة."); return;
            }

            _model.CompanyId = _companyId;
            _model.ProrationBasis = _basis;
            _model.RestDayRate = rate;
            _model.HalfDayHours = half;

            Result result = _services.WorkCalendar.SaveSettings(_model);
            if (result.IsFailure) { Dialogs.Error(result.Error); return; }
            RequestClose?.Invoke(true);
        }
    }
}
