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

        public AttendanceStatus Status { get; }
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
