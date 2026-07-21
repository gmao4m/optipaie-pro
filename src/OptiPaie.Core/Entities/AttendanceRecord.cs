using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One attendance day for one employee. Exactly one record per
    /// (<see cref="EmployeeId"/>, <see cref="WorkDate"/>) — enforced by a unique index —
    /// so attendance can never be duplicated. Always references the SHARED
    /// <c>Employees</c> table; employee data is never copied here.
    /// </summary>
    public sealed class AttendanceRecord : EntityBase
    {
        /// <summary>The shared employee this day belongs to.</summary>
        public long EmployeeId { get; set; }

        /// <summary>The calendar day (time component ignored).</summary>
        public DateTime WorkDate { get; set; }

        public AttendanceStatus Status { get; set; }

        /// <summary>Arrival time, "HH:mm" (null when not applicable).</summary>
        public string CheckIn { get; set; }

        /// <summary>Departure time, "HH:mm" (null when not applicable).</summary>
        public string CheckOut { get; set; }

        /// <summary>Hours actually worked (derived from check-in/out).</summary>
        public decimal WorkedHours { get; set; }

        /// <summary>Minutes late beyond the configured tolerance (derived).</summary>
        public int LateMinutes { get; set; }

        /// <summary>Hours beyond the standard working day (derived) — feeds payroll.</summary>
        public decimal OvertimeHours { get; set; }

        public string Notes { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
