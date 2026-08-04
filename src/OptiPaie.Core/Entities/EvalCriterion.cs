using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>One criterion of an <see cref="EvalTemplate"/>.</summary>
    public sealed class EvalCriterion : EntityBase
    {
        public long TemplateId { get; set; }

        public string Name { get; set; }

        public CriterionCategory Category { get; set; } = CriterionCategory.Behavioral;

        /// <summary>How a non-KPI criterion is scored (stars / 20 / percent). Ignored for KPI.</summary>
        public ScoreType ScoreType { get; set; } = ScoreType.Stars5;

        /// <summary>Importance percentage; used in weighted mode (criteria sum to 100).</summary>
        public decimal WeightPercent { get; set; }

        /// <summary>Default numeric target for a KPI criterion (the objective figure).</summary>
        public decimal? KpiTarget { get; set; }

        /// <summary>For a KPI, whether a higher achieved value is better (false = lower is better).</summary>
        public bool HigherIsBetter { get; set; } = true;

        public int SortOrder { get; set; }

        public bool IsDeleted { get; set; }
    }
}
