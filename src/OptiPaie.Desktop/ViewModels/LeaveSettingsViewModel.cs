using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Leave module parameters. Defaults follow Algerian labour law (loi 90-11):
    /// 2,5 days earned per month worked, capped at 30 per year, Friday/Saturday
    /// excluded from the count.
    /// </summary>
    public sealed class LeaveSettingsViewModel : ObservableObject
    {
        private readonly ILeaveService _service;
        private readonly LeaveSettings _settings;

        private string _daysPerMonth;
        private string _annualCap;
        private bool _excludeRestDays;
        private bool _excludeHolidays;
        private bool _calendarDayCount;
        private bool _referenceJulyToJune;
        private bool _accrualExcludesUnpaid;
        private bool _strictCnasTreatment;
        private string _maternityDays;

        public LeaveSettingsViewModel(ILeaveService service)
        {
            _service = service;

            // Keep the loaded settings and mutate THEM on save, so the six regulatory options are
            // never silently reset to their defaults by a save that only knew about a few fields.
            _settings = service.GetSettings();
            LeaveSettings current = _settings;
            _daysPerMonth = current.DaysPerMonth.ToString("0.##", CultureInfo.InvariantCulture);
            _annualCap = current.AnnualCap.ToString("0.##", CultureInfo.InvariantCulture);
            _excludeRestDays = current.ExcludeRestDays;
            _excludeHolidays = current.ExcludeHolidays;
            _calendarDayCount = current.CalendarDayCount;
            _referenceJulyToJune = current.ReferenceJulyToJune;
            _accrualExcludesUnpaid = current.AccrualExcludesUnpaid;
            _strictCnasTreatment = current.StrictCnasTreatment;
            _maternityDays = current.MaternityDays.ToString("0.##", CultureInfo.InvariantCulture);

            var fr = CultureInfo.GetCultureInfo("fr-FR");
            // Working week starts Sunday in Algeria; list the days in that order.
            DayOfWeek[] order =
            {
                DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
            };
            foreach (DayOfWeek day in order)
            {
                bool off = current.WeekendDays != null && current.WeekendDays.Contains(day);
                WeekendDays.Add(new WeekendDayToggle(day, fr.DateTimeFormat.GetDayName(day), off));
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        /// <summary>Set by the host window: true = saved, false = cancelled.</summary>
        public Action<bool> RequestClose { get; set; }

        public string DaysPerMonth { get => _daysPerMonth; set => Set(ref _daysPerMonth, value); }
        public string AnnualCap { get => _annualCap; set => Set(ref _annualCap, value); }
        public bool ExcludeRestDays { get => _excludeRestDays; set => Set(ref _excludeRestDays, value); }

        /// <summary>Un jour férié légal tombant dans une période de congé n'est pas décompté.</summary>
        public bool ExcludeHolidays { get => _excludeHolidays; set => Set(ref _excludeHolidays, value); }
        /// <summary>Décompter les congés en jours calendaires (au lieu des jours ouvrés).</summary>
        public bool CalendarDayCount { get => _calendarDayCount; set => Set(ref _calendarDayCount, value); }
        /// <summary>Année de référence des congés de juillet à juin (au lieu de l'année civile).</summary>
        public bool ReferenceJulyToJune { get => _referenceJulyToJune; set => Set(ref _referenceJulyToJune, value); }
        /// <summary>Ne pas acquérir de droit à congé pendant les congés sans solde.</summary>
        public bool AccrualExcludesUnpaid { get => _accrualExcludesUnpaid; set => Set(ref _accrualExcludesUnpaid, value); }
        /// <summary>Traitement CNAS strict des congés à charge de la sécurité sociale.</summary>
        public bool StrictCnasTreatment { get => _strictCnasTreatment; set => Set(ref _strictCnasTreatment, value); }
        /// <summary>Durée du congé de maternité, en jours (paramètre informatif).</summary>
        public string MaternityDays { get => _maternityDays; set => Set(ref _maternityDays, value); }

        /// <summary>The company's weekly rest days (a checkbox per day; default Friday + Saturday).</summary>
        public ObservableCollection<WeekendDayToggle> WeekendDays { get; } = new ObservableCollection<WeekendDayToggle>();

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            if (!OptiPaie.Common.Text.FlexibleNumber.TryParse(_daysPerMonth, out decimal perMonth))
            {
                Dialogs.Error("Jours acquis par mois invalide.");
                return;
            }

            if (!OptiPaie.Common.Text.FlexibleNumber.TryParse(_annualCap, out decimal cap))
            {
                Dialogs.Error("Plafond annuel invalide.");
                return;
            }

            if (!OptiPaie.Common.Text.FlexibleNumber.TryParse(_maternityDays, out decimal maternity) || maternity < 0m)
            {
                Dialogs.Error("Durée du congé de maternité invalide.");
                return;
            }

            var weekend = new HashSet<DayOfWeek>(WeekendDays.Where(d => d.IsOff).Select(d => d.Day));

            // Mutate the loaded settings — every field is written, including the six regulatory
            // options, so a save never resets an option the screen didn't previously expose.
            _settings.DaysPerMonth = perMonth;
            _settings.AnnualCap = cap;
            _settings.ExcludeRestDays = _excludeRestDays;
            _settings.WeekendDays = weekend;
            _settings.ExcludeHolidays = _excludeHolidays;
            _settings.CalendarDayCount = _calendarDayCount;
            _settings.ReferenceJulyToJune = _referenceJulyToJune;
            _settings.AccrualExcludesUnpaid = _accrualExcludesUnpaid;
            _settings.StrictCnasTreatment = _strictCnasTreatment;
            _settings.MaternityDays = maternity;

            Result result = _service.SaveSettings(_settings);

            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }
    }

    /// <summary>One weekly day with a toggle for "counts as a company rest day".</summary>
    public sealed class WeekendDayToggle : ObservableObject
    {
        private bool _isOff;

        public WeekendDayToggle(DayOfWeek day, string label, bool isOff)
        {
            Day = day;
            Label = string.IsNullOrEmpty(label) ? day.ToString() : char.ToUpper(label[0]) + label.Substring(1);
            _isOff = isOff;
        }

        public DayOfWeek Day { get; }
        public string Label { get; }
        public bool IsOff { get => _isOff; set => Set(ref _isOff, value); }
    }
}
