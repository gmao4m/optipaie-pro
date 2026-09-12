using System;
using System.Collections.Generic;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Read-only workforce (effectif) analytics for ONE company. Every state figure is at
    /// <see cref="AsOf"/> (today); the flow figures (entries/exits/turnover) span
    /// [<see cref="PeriodStart"/>, <see cref="PeriodEnd"/>]. Each distribution carries, per bucket,
    /// the exact employee ids that compose it — so a click on any figure opens the nominative list.
    /// Aggregated from the shared employee records only; the payroll engine is never involved.
    /// </summary>
    public sealed class WorkforceAnalytics
    {
        public DateTime AsOf { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        // ── key figures (state, at AsOf) ──
        public int Headcount { get; set; }
        public IReadOnlyList<long> HeadcountIds { get; set; } = new List<long>();

        /// <summary>Average age of the active employees whose birth date is known; null when none is known.</summary>
        public decimal? AverageAge { get; set; }
        public int AgeKnownCount { get; set; }
        public int AgeTotalCount { get; set; }

        // ── flow figures (over the period) ──
        public int Entries { get; set; }
        public IReadOnlyList<long> EntryIds { get; set; } = new List<long>();
        public int Exits { get; set; }
        public IReadOnlyList<long> ExitIds { get; set; } = new List<long>();

        public int HeadcountStart { get; set; }
        public int HeadcountEnd { get; set; }

        /// <summary>Average headcount over the period = (start + end) / 2.</summary>
        public decimal AverageHeadcount { get; set; }

        /// <summary>Turnover = exits / average headcount × 100 (0 when the average headcount is 0).</summary>
        public decimal TurnoverRate { get; set; }

        // ── distributions (state, at AsOf) ──
        public WorkforceDistribution ByContract { get; set; } = new WorkforceDistribution();
        public WorkforceDistribution ByCategory { get; set; } = new WorkforceDistribution();
        public WorkforceDistribution ByMaritalStatus { get; set; } = new WorkforceDistribution();
        public WorkforceDistribution ByGender { get; set; } = new WorkforceDistribution();
        public WorkforceDistribution ByAgeBand { get; set; } = new WorkforceDistribution();
        public WorkforceDistribution BySeniority { get; set; } = new WorkforceDistribution();
        public WorkforceDistribution ByDepartment { get; set; } = new WorkforceDistribution();
        public WorkforceDistribution ByPoste { get; set; } = new WorkforceDistribution();
    }

    /// <summary>One distribution axis: its buckets, the total, and how many are "not filled in".</summary>
    public sealed class WorkforceDistribution
    {
        public IReadOnlyList<WorkforceBucket> Buckets { get; set; } = new List<WorkforceBucket>();
        public int Total { get; set; }

        /// <summary>Employees whose value on this axis is not filled in (the « non renseigné » bucket).</summary>
        public int UnknownCount { get; set; }

        /// <summary>True when more than half the employees are unknown on this axis — the split is not reliable.</summary>
        public bool Unreliable { get; set; }
    }

    /// <summary>One slice of a distribution, with the exact employees that compose it.</summary>
    public sealed class WorkforceBucket
    {
        /// <summary>Raw display label for free-text axes (Category, Poste, Department); null for keyed axes.</summary>
        public string Label { get; set; }

        /// <summary>Localization key for enum / band axes (the UI resolves it); null for free-text axes.</summary>
        public string LabelKey { get; set; }

        public int Count { get; set; }
        public IReadOnlyList<long> EmployeeIds { get; set; } = new List<long>();

        /// <summary>True for the « non renseigné » bucket.</summary>
        public bool IsUnknown { get; set; }
    }
}
