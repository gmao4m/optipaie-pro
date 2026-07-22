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

        /// <summary>Weighted average of the criteria on the review's scale (derived).</summary>
        public decimal OverallScore { get; set; }

        /// <summary>General comments / objectives for the next period.</summary>
        public string Comments { get; set; }

        // -- cycle / template linkage (migration 0022; all optional) ------------

        /// <summary>Review cycle this review belongs to, if launched from one.</summary>
        public long? CycleId { get; set; }

        /// <summary>Versioned template this review was created from, if any.</summary>
        public long? TemplateId { get; set; }

        /// <summary>The reviewing employee (their manager), if resolved from org data.</summary>
        public long? ReviewerEmployeeId { get; set; }

        /// <summary>Date the reviewer should submit by (drives due/overdue reminders).</summary>
        public DateTime? DueDate { get; set; }

        /// <summary>Top of the rating scale for this review (default 20, the app convention).</summary>
        public decimal ScaleMax { get; set; } = 20m;

        /// <summary>Employee self-assessment overall score, if captured. Null otherwise.</summary>
        public decimal? SelfScore { get; set; }

        /// <summary>Employee self-assessment comments, if captured.</summary>
        public string SelfComments { get; set; }

        /// <summary>Template kind this review used (for filtering, e.g. probation).</summary>
        public TemplateKind? Kind { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
