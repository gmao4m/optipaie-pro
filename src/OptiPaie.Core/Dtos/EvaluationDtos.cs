using System;
using System.Collections.Generic;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    // ========================================================================
    //  Read models for the Performance (Évaluation) module. All are plain
    //  carriers populated by PerformanceService; the ViewModels bind to them
    //  and localise the enum bands via loc keys.
    // ========================================================================

    /// <summary>One row of the evaluations list / an employee's history.</summary>
    public sealed class EvaluationSummary
    {
        public long EvaluationId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public long PeriodId { get; set; }
        public string PeriodName { get; set; }
        public DateTime PeriodEnd { get; set; }
        /// <summary>Normalised 0-100 overall score.</summary>
        public decimal TotalScore { get; set; }
        public ClassificationBand Band { get; set; }
        public EvaluationStatus Status { get; set; }
        public DateTime? EvaluatedDate { get; set; }
    }

    /// <summary>One row of the templates list.</summary>
    public sealed class TemplateSummary
    {
        public long TemplateId { get; set; }
        public string Name { get; set; }
        public string Department { get; set; }
        public WeightingMode WeightingMode { get; set; }
        public int CriteriaCount { get; set; }
        public bool IsBuiltIn { get; set; }
        public bool IsDefault { get; set; }
    }

    /// <summary>A template with its criteria (the template editor's model).</summary>
    public sealed class TemplateDetail
    {
        public Entities.EvalTemplate Template { get; set; }
        public IReadOnlyList<Entities.EvalCriterion> Criteria { get; set; } = new List<Entities.EvalCriterion>();
    }

    /// <summary>One row of the periods list, with live completion counts.</summary>
    public sealed class PeriodSummary
    {
        public long PeriodId { get; set; }
        public string Name { get; set; }
        public PeriodCadence Cadence { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public PeriodStatus Status { get; set; }
        public int Total { get; set; }
        public int Done { get; set; }
    }

    /// <summary>Everything the evaluation screen needs: the evaluation, its scored lines,
    /// the employee's behaviour log over the period, and the derived band.</summary>
    public sealed class EvaluationDetail
    {
        public Entities.Evaluation Evaluation { get; set; }
        public IReadOnlyList<Entities.EvaluationScore> Scores { get; set; } = new List<Entities.EvaluationScore>();
        public string EmployeeName { get; set; }
        public string EmployeeMeta { get; set; }
        public string PeriodName { get; set; }
        public IReadOnlyList<BehaviorEntry> Behaviors { get; set; } = new List<BehaviorEntry>();
        public int PositiveCount { get; set; }
        public int NegativeCount { get; set; }
        public ClassificationBand Band { get; set; }
    }

    /// <summary>A behaviour-log entry for display.</summary>
    public sealed class BehaviorEntry
    {
        public long Id { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public bool IsPositive { get; set; }
        public string Note { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    /// <summary>A pending evaluation whose period is closing/overdue — drives notifications.</summary>
    public sealed class EvaluationReminder
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string PeriodName { get; set; }
        public DateTime DueDate { get; set; }
        public int DaysLeft { get; set; }
        public bool IsOverdue { get; set; }
    }

    // ---- Reports -----------------------------------------------------------

    /// <summary>A point on a score trend (an employee's or the company's, over periods).</summary>
    public sealed class TrendPoint
    {
        public string PeriodName { get; set; }
        public DateTime Date { get; set; }
        public decimal Score { get; set; }
    }

    /// <summary>A criterion's average score (0-100) — used for strengths / weaknesses.</summary>
    public sealed class CriterionScore
    {
        public string Name { get; set; }
        public decimal Score { get; set; }
    }

    /// <summary>A ranked employee row in a department / company report.</summary>
    public sealed class EmployeeRankRow
    {
        public int Rank { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public decimal Score { get; set; }
        public ClassificationBand Band { get; set; }
    }

    /// <summary>A department's average score.</summary>
    public sealed class DeptScoreRow
    {
        public string Department { get; set; }
        public decimal AverageScore { get; set; }
        public int EmployeeCount { get; set; }
    }

    /// <summary>The best performer of a period (smart touch).</summary>
    public sealed class BestEmployeeInfo
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public decimal Score { get; set; }
        public string PeriodName { get; set; }
    }

    /// <summary>A per-employee report: latest score, trend, strengths/weaknesses,
    /// behaviour and a plain-language recommendation.</summary>
    public sealed class EmployeeReport
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public string Poste { get; set; }
        public bool HasData { get; set; }
        public decimal LatestScore { get; set; }
        public ClassificationBand LatestBand { get; set; }
        public IReadOnlyList<TrendPoint> Trend { get; set; } = new List<TrendPoint>();
        public IReadOnlyList<CriterionScore> Strengths { get; set; } = new List<CriterionScore>();
        public IReadOnlyList<CriterionScore> Weaknesses { get; set; } = new List<CriterionScore>();
        public int PositiveBehaviors { get; set; }
        public int NegativeBehaviors { get; set; }
        public IReadOnlyList<BehaviorEntry> RecentBehaviors { get; set; } = new List<BehaviorEntry>();
        /// <summary>Localisation key of the recommendation (promotion / training / watch).</summary>
        public string RecommendationKey { get; set; }
        /// <summary>True when the score fell over several consecutive periods.</summary>
        public bool IsDeclining { get; set; }
    }

    /// <summary>A department report: average, ranking and who needs support.</summary>
    public sealed class DeptReport
    {
        public string Department { get; set; }
        public decimal AverageScore { get; set; }
        public int EmployeeCount { get; set; }
        public IReadOnlyList<EmployeeRankRow> Ranking { get; set; } = new List<EmployeeRankRow>();
        public IReadOnlyList<EmployeeRankRow> NeedSupport { get; set; } = new List<EmployeeRankRow>();
    }

    /// <summary>The company-wide overview across all departments.</summary>
    public sealed class GeneralReport
    {
        public long CompanyId { get; set; }
        public string CompanyName { get; set; }
        public bool HasData { get; set; }
        public decimal CompanyAverage { get; set; }
        public int EmployeeCount { get; set; }
        public int EvaluatedCount { get; set; }
        public IReadOnlyList<DeptScoreRow> Departments { get; set; } = new List<DeptScoreRow>();
        public IReadOnlyList<EmployeeRankRow> TopPerformers { get; set; } = new List<EmployeeRankRow>();
        public IReadOnlyList<EmployeeRankRow> NeedSupport { get; set; } = new List<EmployeeRankRow>();
        public IReadOnlyList<TrendPoint> Trend { get; set; } = new List<TrendPoint>();
        public BestEmployeeInfo BestEmployee { get; set; }
    }

    // ---- Overview (the control-center tab) ---------------------------------

    /// <summary>An employee whose latest score moved vs the previous period.</summary>
    public sealed class MoverRow
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public decimal Score { get; set; }
        public decimal Delta { get; set; }
    }

    /// <summary>An actionable item on the overview: a decline to watch, or a period to finish.</summary>
    public sealed class OverviewAlert
    {
        /// <summary>"decline" | "pending" | "overdue".</summary>
        public string Kind { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public long PeriodId { get; set; }
        public string PeriodName { get; set; }
        public int Count { get; set; }
        /// <summary>Pill/severity bucket: "danger" | "pending".</summary>
        public string Severity { get; set; }
    }

    /// <summary>Everything the Overview (control-center) tab shows in one pass.</summary>
    public sealed class OverviewData
    {
        public bool HasData { get; set; }
        public decimal CompanyAverage { get; set; }
        public ClassificationBand CompanyBand { get; set; }
        public int EmployeeCount { get; set; }
        public int EvaluatedCount { get; set; }
        public int PendingCount { get; set; }
        public int NotEvaluatedCount { get; set; }
        public BestEmployeeInfo BestEmployee { get; set; }
        public IReadOnlyList<MoverRow> Improved { get; set; } = new List<MoverRow>();
        public IReadOnlyList<MoverRow> Declined { get; set; } = new List<MoverRow>();
        public IReadOnlyList<EmployeeRankRow> NeedSupport { get; set; } = new List<EmployeeRankRow>();
        public IReadOnlyList<OverviewAlert> Alerts { get; set; } = new List<OverviewAlert>();
        public IReadOnlyList<BehaviorEntry> RecentActivity { get; set; } = new List<BehaviorEntry>();
        public long ActivePeriodId { get; set; }
        public string ActivePeriodName { get; set; }
        public bool HasActivePeriod { get; set; }
    }
}
