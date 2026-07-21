using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One rated line of a <see cref="PerformanceReview"/>. Scored on a /20 scale and
    /// weighted; the review's overall score is the weighted average of its criteria.
    /// </summary>
    public sealed class PerformanceCriterion : EntityBase
    {
        public long ReviewId { get; set; }

        /// <summary>Criterion label (e.g. "Qualité du travail").</summary>
        public string Label { get; set; }

        /// <summary>Relative weight in the overall score (default 1).</summary>
        public decimal Weight { get; set; }

        /// <summary>Score on a /20 scale.</summary>
        public decimal Score { get; set; }

        /// <summary>Optional per-criterion comment.</summary>
        public string Comment { get; set; }

        /// <summary>Display order in the review.</summary>
        public int SortOrder { get; set; }

        public bool IsDeleted { get; set; }
    }
}
