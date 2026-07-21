using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A performance review of one employee for a period. Always references the SHARED
    /// <c>Employees</c> table — no employee or company data is copied. The overall score
    /// is derived from the weighted criteria, and the attendance context (absences,
    /// lates) is pulled live from the Attendance module, never duplicated here.
    /// </summary>
    public sealed class PerformanceReview : EntityBase
    {
        /// <summary>The shared employee being reviewed.</summary>
        public long EmployeeId { get; set; }

        /// <summary>Review period year.</summary>
        public int PeriodYear { get; set; }

        /// <summary>Human label of the period, e.g. "2026" or "2026 - S1".</summary>
        public string PeriodLabel { get; set; }

        public PerformanceStatus Status { get; set; }

        public DateTime ReviewDate { get; set; }

        /// <summary>Name of the person who conducted the review.</summary>
        public string Reviewer { get; set; }

        /// <summary>Weighted average of the criteria on a /20 scale (derived).</summary>
        public decimal OverallScore { get; set; }

        /// <summary>General comments / objectives for the next period.</summary>
        public string Comments { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
