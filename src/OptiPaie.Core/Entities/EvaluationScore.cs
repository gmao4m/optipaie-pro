using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One scored criterion line of an <see cref="Evaluation"/> (a snapshot of the template
    /// criterion at evaluation time). <see cref="NormalizedScore"/> (0-100) is what the
    /// evaluation total is built from.
    /// </summary>
    public sealed class EvaluationScore : EntityBase
    {
        public long EvaluationId { get; set; }

        public string CriterionName { get; set; }

        public CriterionCategory Category { get; set; } = CriterionCategory.Behavioral;

        public ScoreType ScoreType { get; set; } = ScoreType.Stars5;

        /// <summary>Weight percentage snapshot (used when the evaluation is in weighted mode).</summary>
        public decimal WeightPercent { get; set; }

        /// <summary>Raw value the evaluator entered (stars 1-5, /20 or %); null = not yet scored.</summary>
        public decimal? RawValue { get; set; }

        /// <summary>KPI objective figure (for a KPI criterion).</summary>
        public decimal? KpiTarget { get; set; }

        /// <summary>KPI achieved figure (for a KPI criterion).</summary>
        public decimal? KpiActual { get; set; }

        public bool HigherIsBetter { get; set; } = true;

        /// <summary>Computed 0-100 score for this criterion.</summary>
        public decimal NormalizedScore { get; set; }

        public string Note { get; set; }

        public int SortOrder { get; set; }

        public bool IsDeleted { get; set; }
    }
}
