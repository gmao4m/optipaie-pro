using System;
using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    // ===== Templates =========================================================

    /// <summary>A template as shown in the picker gallery (name, department, criteria count).</summary>
    public sealed class TemplateSummary
    {
        public long TemplateId { get; set; }
        public string GroupKey { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public TemplateKind Kind { get; set; }
        public string KindLabel { get; set; }
        public string DepartmentTag { get; set; }
        public int CriteriaCount { get; set; }
        public decimal ScaleMax { get; set; }
        public bool IsBuiltIn { get; set; }
        public int Version { get; set; }
    }

    /// <summary>A template with its criteria and the total of its weights (should be 100).</summary>
    public sealed class TemplateDetail
    {
        public PerformanceTemplate Template { get; set; }
        public IReadOnlyList<PerformanceTemplateCriterion> Criteria { get; set; } = new List<PerformanceTemplateCriterion>();
        public decimal WeightTotal { get; set; }
    }

    // ===== Cycles ============================================================

    /// <summary>What to include when launching a review cycle (bulk-assign).</summary>
    public sealed class CycleLaunchRequest
    {
        public long CompanyId { get; set; }
        public string Name { get; set; }
        public PerformanceCycleType CycleType { get; set; } = PerformanceCycleType.Quarterly;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime? Deadline { get; set; }
        public bool SelfAssessment { get; set; }
        public int PeriodYear { get; set; }
        public string PeriodLabel { get; set; }

        /// <summary>Explicit employees to include. If null/empty, <see cref="Departments"/> is used.</summary>
        public IReadOnlyList<long> EmployeeIds { get; set; }

        /// <summary>Departments to include (all active employees in them). Null = whole company.</summary>
        public IReadOnlyList<string> Departments { get; set; }

        /// <summary>Fallback template group when a department has no configured default.</summary>
        public string DefaultTemplateGroupKey { get; set; }
    }

    /// <summary>A cycle with its live completion figures.</summary>
    public sealed class CycleSummary
    {
        public long CycleId { get; set; }
        public string Name { get; set; }
        public PerformanceCycleType CycleType { get; set; }
        public string CycleTypeLabel { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime? Deadline { get; set; }
        public PerformanceCycleStatus Status { get; set; }
        public string StatusLabel { get; set; }
        public bool SelfAssessment { get; set; }
        public int TotalReviews { get; set; }
        public int CompletedReviews { get; set; }
        public decimal CompletionPercent { get; set; }
    }

    public sealed class CycleDetail
    {
        public PerformanceCycle Cycle { get; set; }
        public IReadOnlyList<CycleReviewRow> Reviews { get; set; } = new List<CycleReviewRow>();
        public IReadOnlyList<DeptCompletionRow> ByDepartment { get; set; } = new List<DeptCompletionRow>();
        public int Total { get; set; }
        public int Completed { get; set; }
        public decimal CompletionPercent { get; set; }
    }

    public sealed class CycleReviewRow
    {
        public long ReviewId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public string Reviewer { get; set; }
        public PerformanceStatus Status { get; set; }
        public string StatusLabel { get; set; }
        public decimal OverallScore { get; set; }
        public decimal ScaleMax { get; set; }
        public string Rating { get; set; }
        public DateTime? DueDate { get; set; }
        public bool IsOverdue { get; set; }
    }

    public sealed class DeptCompletionRow
    {
        public string Department { get; set; }
        public int Total { get; set; }
        public int Completed { get; set; }
        public decimal CompletionPercent { get; set; }
    }

    // ===== Calibration =======================================================

    /// <summary>Rating distribution across every department, for the HR calibration screen.</summary>
    public sealed class CalibrationView
    {
        public string ScopeLabel { get; set; }
        public int ReviewCount { get; set; }
        public decimal CompanyAveragePercent { get; set; }

        /// <summary>Company-wide band counts (index 0 = Insuffisant .. 4 = Excellent).</summary>
        public int[] Distribution { get; set; } = new int[5];

        public IReadOnlyList<CalibrationDeptRow> Departments { get; set; } = new List<CalibrationDeptRow>();
    }

    public sealed class CalibrationDeptRow
    {
        public string Department { get; set; }
        public int ReviewCount { get; set; }
        public decimal AveragePercent { get; set; }

        /// <summary>Difference vs the company average (percentage points).</summary>
        public decimal DeltaVsCompany { get; set; }

        /// <summary>Band counts (index 0 = Insuffisant .. 4 = Excellent).</summary>
        public int[] Distribution { get; set; } = new int[5];

        /// <summary>Rating unusually high vs the company (possible leniency).</summary>
        public bool IsOutlierHigh { get; set; }

        /// <summary>Rating unusually low vs the company (possible harshness).</summary>
        public bool IsOutlierLow { get; set; }
    }

    // ===== Company dashboard =================================================

    public sealed class PerformanceDashboard
    {
        public long CompanyId { get; set; }
        public int ReviewCount { get; set; }
        public decimal CompanyAveragePercent { get; set; }
        public IReadOnlyList<PerformerRow> TopPerformers { get; set; } = new List<PerformerRow>();
        public IReadOnlyList<PerformerRow> BottomPerformers { get; set; } = new List<PerformerRow>();
        public IReadOnlyList<DeptScoreRow> DepartmentAverages { get; set; } = new List<DeptScoreRow>();
        public IReadOnlyList<TrendPoint> Trend { get; set; } = new List<TrendPoint>();
    }

    public sealed class PerformerRow
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public decimal LatestScore { get; set; }
        public decimal ScaleMax { get; set; }
        public decimal ScorePercent { get; set; }
        public string Rating { get; set; }
    }

    public sealed class DeptScoreRow
    {
        public string Department { get; set; }
        public int ReviewCount { get; set; }
        public decimal AveragePercent { get; set; }
    }

    public sealed class TrendPoint
    {
        public string Label { get; set; }
        public int Year { get; set; }
        public decimal AveragePercent { get; set; }
        public int ReviewCount { get; set; }
    }

    // ===== Comparison ========================================================

    public sealed class EmployeeComparison
    {
        public IReadOnlyList<ComparisonColumn> Employees { get; set; } = new List<ComparisonColumn>();
    }

    public sealed class ComparisonColumn
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public string Poste { get; set; }
        public bool HasReviews { get; set; }
        public decimal LatestScore { get; set; }
        public decimal ScaleMax { get; set; }
        public decimal LatestPercent { get; set; }
        public string Rating { get; set; }
        public int ReviewCount { get; set; }
        public decimal AveragePercent { get; set; }
        public int ActiveGoals { get; set; }
        public decimal GoalCompletionPercent { get; set; }
        public IReadOnlyList<PerformanceSummary> History { get; set; } = new List<PerformanceSummary>();
    }

    // ===== Career timeline & goals ==========================================

    public sealed class CareerTimeline
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public IReadOnlyList<CareerTimelineItem> Items { get; set; } = new List<CareerTimelineItem>();
    }

    public sealed class CareerTimelineItem
    {
        public DateTime Date { get; set; }
        /// <summary>"review", "goal", "promotion" or "reward".</summary>
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public string ValueText { get; set; }
        public long? ReferenceId { get; set; }
    }

    public sealed class GoalRow
    {
        public long GoalId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Title { get; set; }
        public string TargetMetric { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal ProgressPercent { get; set; }
        public PerformanceGoalStatus Status { get; set; }
        public string StatusLabel { get; set; }
        public bool IsOverdue { get; set; }
    }

    // ===== Reminders (surfaced through the Notifications engine) =============

    public sealed class PerformanceReminder
    {
        public long ReviewId { get; set; }
        public long CompanyId { get; set; }
        public string EmployeeName { get; set; }
        public string Reviewer { get; set; }
        public DateTime DueDate { get; set; }
        public int DaysLeft { get; set; }
        public bool IsOverdue { get; set; }
    }

    /// <summary>
    /// A promotion logged in Performance whose position has not yet been reflected on the
    /// employee's contract — surfaced as a "prepare a contract amendment" prompt (never edits).
    /// </summary>
    public sealed class ContractAmendmentPrompt
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string OldPosition { get; set; }
        public string NewPosition { get; set; }
        public DateTime Date { get; set; }
    }
}
