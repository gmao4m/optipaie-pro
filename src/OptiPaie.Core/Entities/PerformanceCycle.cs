using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A review cycle: the reviews launched together for a company over a date range.
    /// The completion percentage is derived live from the reviews that reference it —
    /// never stored — so it is always accurate.
    /// </summary>
    public sealed class PerformanceCycle : EntityBase
    {
        public long CompanyId { get; set; }

        public string Name { get; set; }

        public PerformanceCycleType CycleType { get; set; } = PerformanceCycleType.Quarterly;

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        /// <summary>Date reviewers should complete their reviews by. Null if open-ended.</summary>
        public DateTime? Deadline { get; set; }

        public PerformanceCycleStatus Status { get; set; } = PerformanceCycleStatus.Draft;

        /// <summary>Whether a self-assessment is captured alongside each review.</summary>
        public bool SelfAssessment { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
