namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Monthly attendance totals for one employee. This is the object the Payroll
    /// module consumes (overtime hours, absences) — computed from the shared
    /// attendance records, never stored twice.
    /// </summary>
    public sealed class AttendanceSummary
    {
        public long EmployeeId { get; set; }

        /// <summary>Display name (filled when summarising a whole company).</summary>
        public string EmployeeName { get; set; }

        public int Year { get; set; }
        public int Month { get; set; }

        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        public int LeaveDays { get; set; }
        public int HolidayDays { get; set; }
        public int RestDays { get; set; }

        /// <summary>Number of days flagged late.</summary>
        public int LateCount { get; set; }

        /// <summary>Total minutes late over the month.</summary>
        public int LateMinutes { get; set; }

        public decimal WorkedHours { get; set; }

        /// <summary>Total overtime hours — fed into the payroll request.</summary>
        public decimal OvertimeHours { get; set; }

        /// <summary>Days with a recorded attendance row.</summary>
        public int RecordedDays { get; set; }
    }

    /// <summary>Module settings driving the attendance calculations.</summary>
    public sealed class AttendanceSettings
    {
        /// <summary>Official start of the working day, "HH:mm" (default 08:00).</summary>
        public string StandardStart { get; set; } = "08:00";

        /// <summary>Standard working hours per day (default 8).</summary>
        public decimal StandardHours { get; set; } = 8m;

        /// <summary>Grace period before an arrival counts as late (default 10 min).</summary>
        public int LateToleranceMinutes { get; set; } = 10;
    }
}
