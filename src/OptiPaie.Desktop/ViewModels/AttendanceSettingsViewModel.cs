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
    /// Attendance module parameters. These three values drive every calculation the
    /// module (and therefore payroll) produces, so they are edited in one place.
    /// </summary>
    public sealed class AttendanceSettingsViewModel : ObservableObject
    {
        private readonly IAttendanceService _service;

        private string _standardStart;
        private string _standardHours;
        private string _tolerance;

        public AttendanceSettingsViewModel(IAttendanceService service)
        {
            _service = service;

            AttendanceSettings current = service.GetSettings();
            _standardStart = current.StandardStart;
            _standardHours = current.StandardHours.ToString("0.##", CultureInfo.InvariantCulture);
            _tolerance = current.LateToleranceMinutes.ToString(CultureInfo.InvariantCulture);

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        /// <summary>Set by the host window: true = saved, false = cancelled.</summary>
        public Action<bool> RequestClose { get; set; }

        public string StandardStart { get => _standardStart; set => Set(ref _standardStart, value); }
        public string StandardHours { get => _standardHours; set => Set(ref _standardHours, value); }
        public string LateToleranceMinutes { get => _tolerance; set => Set(ref _tolerance, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            if (!decimal.TryParse(_standardHours, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal hours))
            {
                Dialogs.Error("Nombre d'heures standard invalide.");
                return;
            }

            if (!int.TryParse(_tolerance, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tolerance))
            {
                Dialogs.Error("Tolérance de retard invalide.");
                return;
            }

            Result result = _service.SaveSettings(new AttendanceSettings
            {
                StandardStart = (_standardStart ?? string.Empty).Trim(),
                StandardHours = hours,
                LateToleranceMinutes = tolerance
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
