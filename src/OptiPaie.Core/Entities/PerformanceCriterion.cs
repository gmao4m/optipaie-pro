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

        /// <summary>Score on the review's scale (see <see cref="PerformanceReview.ScaleMax"/>). For a KPI criterion this is derived from target/achieved.</summary>
        public decimal Score { get; set; }

        /// <summary>Behavioral (rated directly) or KPI/numeric (derived from target &amp; achieved).</summary>
        public Enums.CriterionType CriterionType { get; set; } = Enums.CriterionType.Behavioral;

        /// <summary>KPI only: the objective for the period.</summary>
        public decimal? KpiTarget { get; set; }

        /// <summary>KPI only: what was actually achieved.</summary>
        public decimal? KpiAchieved { get; set; }

        /// <summary>KPI only: true when a higher achieved value is better.</summary>
        public bool HigherIsBetter { get; set; } = true;

        /// <summary>Optional per-criterion comment.</summary>
        public string Comment { get; set; }

        /// <summary>Display order in the review.</summary>
        public int SortOrder { get; set; }

        public bool IsDeleted { get; set; }
    }
}
