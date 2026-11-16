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

        // Day counters are DECIMAL (Commit 3): a day can be a half (0.5). A whole day stays an exact
        // integer value (3m == 3), so existing data is unchanged. These flow to payroll — truncating any
        // of them to int would silently over/under-pay, which every Commit-3 test is aimed at.
        public decimal PresentDays { get; set; }
        public decimal AbsentDays { get; set; }
        public decimal LeaveDays { get; set; }
        public decimal HolidayDays { get; set; }
        public decimal RestDays { get; set; }

        /// <summary>Number of days flagged late (a whole-day flag — not fractional).</summary>
        public int LateCount { get; set; }

        /// <summary>Total minutes late over the month (minutes, not a day count).</summary>
        public int LateMinutes { get; set; }

        public decimal WorkedHours { get; set; }

        /// <summary>Total overtime hours — fed into the payroll request.</summary>
        public decimal OvertimeHours { get; set; }

        /// <summary>Days with a recorded attendance row.</summary>
        public decimal RecordedDays { get; set; }

        /// <summary>Days actually PAID by payroll — mirrors the engine gate (monthDays − AbsentDays when
        /// any day was recorded, else the full month). Derived, never stored; reconciles the Synthèse with
        /// what the payslip pays in the exception-only pointage flow. Decimal, so a half day is exact.</summary>
        public decimal PaidDays
        {
            get
            {
                if (Year <= 0 || Month < 1 || Month > 12) return 0m;
                decimal monthDays = System.DateTime.DaysInMonth(Year, Month);
                return RecordedDays > 0m ? System.Math.Max(0m, monthDays - AbsentDays) : monthDays;
            }
        }
    }

    /// <summary>A single status-only entry for the fast matrix / bulk operations.</summary>
    public sealed class AttendanceDayStatus
    {
        public AttendanceDayStatus() { }

        public AttendanceDayStatus(long employeeId, System.DateTime workDate, OptiPaie.Core.Enums.AttendanceStatus status)
        {
            EmployeeId = employeeId;
            WorkDate = workDate;
            Status = status;
        }

        public long EmployeeId { get; set; }
        public System.DateTime WorkDate { get; set; }
        public OptiPaie.Core.Enums.AttendanceStatus Status { get; set; }
    }

    /// <summary>Company-wide attendance KPIs for a month (derived).</summary>
    public sealed class AttendanceKpis
    {
        public int Employees { get; set; }
        public int WorkingDays { get; set; }

        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public int LateCount { get; set; }
        public int LeaveCount { get; set; }
        public int MissionCount { get; set; }

        /// <summary>Present ÷ (working days × employees), as a percentage 0–100.</summary>
        public decimal AttendanceRate { get; set; }
        public decimal AbsenceRate { get; set; }
        public decimal LateRate { get; set; }

        public decimal OvertimeHours { get; set; }

        /// <summary>Today's snapshot (only meaningful when the month contains today).</summary>
        public int PresentToday { get; set; }
        public int OnLeaveToday { get; set; }
        public int OnMissionToday { get; set; }
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
