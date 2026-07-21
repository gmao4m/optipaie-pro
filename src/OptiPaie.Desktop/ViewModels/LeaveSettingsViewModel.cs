using System;
using System.Globalization;
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

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        /// <summary>Set by the host window: true = saved, false = cancelled.</summary>
        public Action<bool> RequestClose { get; set; }

        public string DaysPerMonth { get => _daysPerMonth; set => Set(ref _daysPerMonth, value); }
        public string AnnualCap { get => _annualCap; set => Set(ref _annualCap, value); }
        public bool ExcludeRestDays { get => _excludeRestDays; set => Set(ref _excludeRestDays, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            if (!decimal.TryParse(_daysPerMonth, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal perMonth))
            {
                Dialogs.Error("Jours acquis par mois invalide.");
                return;
            }

            if (!decimal.TryParse(_annualCap, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal cap))
            {
                Dialogs.Error("Plafond annuel invalide.");
                return;
            }

            Result result = _service.SaveSettings(new LeaveSettings
            {
                DaysPerMonth = perMonth,
                AnnualCap = cap,
                ExcludeRestDays = _excludeRestDays
            });

            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }
    }
}
