using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// An employee goal / KPI with a live progress percentage. Goals surface inside the
    /// next review cycle as a discussion point (the reviewer sees completion history when
    /// scoring). References the shared Employees table; no employee data is copied.
    /// </summary>
    public sealed class PerformanceGoal : EntityBase
    {
        public long EmployeeId { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        /// <summary>Target metric or milestone (free text, e.g. "12 ventes / mois").</summary>
        public string TargetMetric { get; set; }

        public DateTime? DueDate { get; set; }

        /// <summary>Live progress, 0..100.</summary>
        public decimal ProgressPercent { get; set; }

        public PerformanceGoalStatus Status { get; set; } = PerformanceGoalStatus.Active;

        /// <summary>Cycle this goal was created from / discussed in, if any.</summary>
        public long? SourceCycleId { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
