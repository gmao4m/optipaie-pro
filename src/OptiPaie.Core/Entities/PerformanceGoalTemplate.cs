namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A department-level goal template so recurring goals (e.g. a standard sales target)
    /// aren't rebuilt from scratch each cycle. <see cref="CompanyId"/> null = shared.
    /// </summary>
    public sealed class PerformanceGoalTemplate : EntityBase
    {
        public long? CompanyId { get; set; }

        public string DepartmentTag { get; set; }

        public string Title { get; set; }

        public string TargetMetric { get; set; }

        public string Description { get; set; }

        public bool IsDeleted { get; set; }
    }
}
