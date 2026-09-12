using System.Windows.Media;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels.Attendance
{
    /// <summary>One entry of the status "paint brush" palette in the toolbar.</summary>
    public sealed class StatusBrushViewModel : ObservableObject
    {
        private bool _isSelected;

        public StatusBrushViewModel(AttendanceStatus status)
        {
            Status = status;
            Fill = AttendanceAppearance.Fill(status);
            Label = AttendanceAppearance.Label(status);
            Letter = AttendanceAppearance.Letter(status, false);
        }

        private StatusBrushViewModel()
        {
            IsEraser = true;
            Fill = AttendanceAppearance.EraserFill;
            Label = OptiPaie.Desktop.Localization.TranslationSource.Instance["Att_Erase"];
            Letter = "×";
        }

        /// <summary>The eraser brush — clears a day (removes its attendance record) instead of painting a status.</summary>
        public static StatusBrushViewModel Eraser() => new StatusBrushViewModel();

        public AttendanceStatus Status { get; }

        /// <summary>True for the eraser entry; then <see cref="Status"/> carries no meaning.</summary>
        public bool IsEraser { get; }

        public Brush Fill { get; }
        public string Label { get; }
        public string Letter { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
