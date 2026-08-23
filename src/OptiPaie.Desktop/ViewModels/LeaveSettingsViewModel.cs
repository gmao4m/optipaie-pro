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

        private string _daysPerMonth;
        private string _annualCap;
        private bool _excludeRestDays;

        public LeaveSettingsViewModel(ILeaveService service)
        {
            _service = service;

            LeaveSettings current = service.GetSettings();
            _daysPerMonth = current.DaysPerMonth.ToString("0.##", CultureInfo.InvariantCulture);
            _annualCap = current.AnnualCap.ToString("0.##", CultureInfo.InvariantCulture);
            _excludeRestDays = current.ExcludeRestDays;

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

            var weekend = new HashSet<DayOfWeek>(WeekendDays.Where(d => d.IsOff).Select(d => d.Day));

            Result result = _service.SaveSettings(new LeaveSettings
            {
                DaysPerMonth = perMonth,
                AnnualCap = cap,
                ExcludeRestDays = _excludeRestDays,
                WeekendDays = weekend
            });

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
