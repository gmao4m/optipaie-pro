namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One weighted criterion of a <see cref="PerformanceTemplate"/>. Weights are
    /// percentages that should sum to 100 across a template. When a review is created
    /// from a template these are snapshotted into <see cref="PerformanceCriterion"/> rows
    /// so later template edits never rewrite a scored review.
    /// </summary>
    public sealed class PerformanceTemplateCriterion : EntityBase
    {
        public long TemplateId { get; set; }

        public string Label { get; set; }

        /// <summary>Percentage weight in the overall score (0..100).</summary>
        public decimal WeightPercent { get; set; }

        public int SortOrder { get; set; }

        public bool IsDeleted { get; set; }
    }
}
