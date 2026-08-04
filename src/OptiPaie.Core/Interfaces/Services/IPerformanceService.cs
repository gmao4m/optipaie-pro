using System;
using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// The Performance (Évaluation) module service. Owns the fair-scoring rules: every
    /// criterion is normalised to 0-100 whatever its type (stars / 20 / percent / KPI), then
    /// combined equally (simple mode) or by weight (weighted mode) into one 0-100 total, mapped
    /// to a five-band classification. Also owns the behaviour log, evaluation periods, per-report
    /// aggregation and the "smart" hints. Reads employee/department data; never writes payroll.
    /// </summary>
    public interface IPerformanceService
    {
        // ===================== TEMPLATES =====================================

        /// <summary>Templates visible to a company (its own + built-ins), with criteria counts.</summary>
        IReadOnlyList<TemplateSummary> GetTemplates(long companyId);

        /// <summary>A template with its criteria, for the editor.</summary>
        TemplateDetail GetTemplateDetail(long templateId);

        /// <summary>The template a company should use for a department: its department template →
        /// its default → the built-in base. Never null when the built-in seed is present.</summary>
        EvalTemplate ResolveTemplate(long companyId, string department);

        /// <summary>Creates or updates a company template and replaces its criteria wholesale.
        /// In weighted mode the criteria weights must sum to 100 (±0.5).</summary>
        Result<long> SaveTemplate(EvalTemplate template, IEnumerable<EvalCriterion> criteria);

        /// <summary>Copies any visible template into a new editable company template.</summary>
        Result<long> DuplicateTemplate(long sourceTemplateId, long companyId, string newName, string department);

        /// <summary>Marks one company template as the default (clears the others).</summary>
        Result SetDefaultTemplate(long companyId, long templateId);

        /// <summary>Soft-deletes a company template (built-ins are read-only).</summary>
        Result DeleteTemplate(long templateId);

        // ===================== PERIODS =======================================

        IReadOnlyList<PeriodSummary> GetPeriods(long companyId);

        EvalPeriod GetPeriod(long periodId);

        Result<long> SavePeriod(EvalPeriod period);

        Result ClosePeriod(long periodId);

        Result ReopenPeriod(long periodId);

        Result DeletePeriod(long periodId);

        // ===================== EVALUATIONS ===================================

        /// <summary>One row per active employee of the company for a period: their evaluation and
        /// status if started, or a pending placeholder (EvaluationId = 0) if not.</summary>
        IReadOnlyList<EvaluationSummary> GetEvaluationBoard(long periodId);

        /// <summary>Full data for the evaluation screen (scores + the employee's behaviour log
        /// over the period window + derived band).</summary>
        EvaluationDetail GetEvaluationDetail(long evaluationId);

        /// <summary>Creates a pending evaluation for an employee in a period, snapshotting the
        /// resolved template's criteria into unscored lines. Returns the existing one if any.</summary>
        Result<long> CreateEvaluation(long periodId, long employeeId, long? templateId);

        /// <summary>Saves a draft evaluation: recomputes each line's normalised score and the
        /// overall total, then persists (score lines replaced wholesale).</summary>
        Result SaveEvaluation(Evaluation evaluation, IEnumerable<EvaluationScore> scores);

        Result CompleteEvaluation(long evaluationId);

        Result ReopenEvaluation(long evaluationId);

        Result DeleteEvaluation(long evaluationId);

        // ----- scoring helpers (pure; power the live preview) ----------------

        /// <summary>Normalises one scored line to 0-100 from its type/value (or KPI target/achieved).</summary>
        decimal ComputeLineScore(EvaluationScore line);

        /// <summary>Combines normalised line scores into a 0-100 total (equal or by weight).</summary>
        decimal ComputeTotal(IReadOnlyList<EvaluationScore> scores, WeightingMode mode);

        /// <summary>Maps a 0-100 total to its fairness band.</summary>
        ClassificationBand Classify(decimal totalScore);

        // ===================== BEHAVIOUR LOG =================================

        Result<long> LogBehavior(long companyId, long employeeId, bool isPositive, string note, DateTime occurredAt);

        IReadOnlyList<BehaviorEntry> GetBehaviors(long employeeId);

        IReadOnlyList<BehaviorEntry> GetCompanyBehaviors(long companyId);

        Result DeleteBehavior(long behaviorId);

        // ===================== REPORTS & SMART ===============================

        EmployeeReport GetEmployeeReport(long employeeId);

        DeptReport GetDeptReport(long companyId, string department);

        GeneralReport GetGeneralReport(long companyId);

        /// <summary>Best performer of the company's most recent evaluated period (may be null).</summary>
        BestEmployeeInfo GetBestEmployee(long companyId);

        /// <summary>Everything the Overview (control-center) tab needs in one pass: company average
        /// + band, best, need-support, movers (improved/declined), pending/not-evaluated counts,
        /// actionable alerts, and the recent behaviour activity.</summary>
        OverviewData GetOverview(long companyId);

        // ===================== CONSUMED BY OTHER MODULES =====================

        /// <summary>An employee's completed evaluations, most recent first (the 360° profile).</summary>
        IReadOnlyList<EvaluationSummary> GetByEmployee(long employeeId);

        /// <summary>Pending evaluations in open periods that are closing or overdue (notifications).</summary>
        IReadOnlyList<EvaluationReminder> GetReminders(long companyId, DateTime asOf);
    }
}
