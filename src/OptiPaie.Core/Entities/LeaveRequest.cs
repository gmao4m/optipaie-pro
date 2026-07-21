using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One leave request for one employee. Always references the SHARED
    /// <c>Employees</c> table — no employee or company data is copied here. Approving
    /// a request writes the corresponding days into the Attendance module, so payroll
    /// sees them without any import or manual synchronisation.
    /// </summary>
    public sealed class LeaveRequest : EntityBase
    {
        /// <summary>The shared employee taking the leave.</summary>
        public long EmployeeId { get; set; }

        public LeaveType Type { get; set; }

        public LeaveStatus Status { get; set; }

        /// <summary>First day of leave (inclusive).</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Last day of leave (inclusive).</summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Leave days actually consumed — rest days (Friday/Saturday) excluded.
        /// Derived by the service, never entered by hand.
        /// </summary>
        public decimal Days { get; set; }

        /// <summary>Reason given by the employee.</summary>
        public string Reason { get; set; }

        /// <summary>Free note from whoever decided (approval or refusal).</summary>
        public string DecisionNote { get; set; }

        /// <summary>When the request was approved or rejected.</summary>
        public DateTime? DecidedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
