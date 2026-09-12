using System;
using System.Windows.Media;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels.Attendance
{
    /// <summary>
    /// One day-cell of one employee in the attendance matrix. Holds the status and the
    /// derived appearance (colour + letter); painting a status updates the visuals
    /// instantly and the owning row/board persists it (auto-save).
    /// </summary>
    public sealed class MatrixCellViewModel : ObservableObject
    {
        private AttendanceStatus? _status;

        public MatrixCellViewModel(long employeeId, DateTime date, bool isWeekend, bool isFuture, AttendanceStatus? status, bool isLeaveLinked = false)
        {
            EmployeeId = employeeId;
            Date = date;
            Day = date.Day;
            IsWeekend = isWeekend;
            IsFuture = isFuture;
            _status = status;
            IsLeaveLinked = isLeaveLinked;
        }

        public long EmployeeId { get; }
        public DateTime Date { get; }
        public int Day { get; }
        public bool IsWeekend { get; }

        /// <summary>Future days are read-only (the matrix never records the future).</summary>
        public bool IsFuture { get; }

        /// <summary>
        /// True when this day was written by the Leave module (an approved leave, marked "[Congé]").
        /// Such cells are PROTECTED from bulk attendance painting so a synced leave is never silently
        /// overwritten (which would turn an unpaid leave into a paid day). A deliberate single-cell
        /// repaint still changes it and detaches it from the leave (the service clears the marker).
        /// </summary>
        public bool IsLeaveLinked { get; private set; }

        public AttendanceStatus? Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    Raise(nameof(Status));
                    Raise(nameof(Background));
                    Raise(nameof(Letter));
                    Raise(nameof(Ink));
                    Raise(nameof(Tooltip));
                }
            }
        }

        /// <summary>Sets the status in-memory (persistence is the board's job). Returns true if it changed.</summary>
        public bool Paint(AttendanceStatus status)
        {
            if (IsFuture || _status == status)
            {
                return false;
            }

            Status = status;
            IsLeaveLinked = false; // a deliberate repaint detaches the day from its synced leave
            return true;
        }

        /// <summary>Erases the status in-memory (the board removes the record). Returns true if it changed.
        /// A leave-linked day is protected upstream, so this is only ever reached for a normal cell.</summary>
        public bool Clear()
        {
            if (IsFuture || !_status.HasValue)
            {
                return false;
            }

            Status = null;
            return true;
        }

        public Brush Background => AttendanceAppearance.Background(_status, IsWeekend, IsFuture);
        public string Letter => AttendanceAppearance.Letter(_status, IsWeekend);
        public Brush Ink => AttendanceAppearance.Ink(_status);

        public string Tooltip
        {
            get
            {
                string date = Date.ToString("dddd d MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));
                string status = _status.HasValue ? AttendanceAppearance.Label(_status.Value)
                    : (IsWeekend ? "Repos / week-end" : "—");
                return date + " · " + status;
            }
        }
    }
}
