using System;
using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// The Performance &amp; Career module service: reviews and scoring, the versioned
    /// template library, review cycles, goals, calibration, the company dashboard,
    /// promotions/rewards and employee comparison. Owns scoring (weighted average on each
    /// review's configurable scale) and the rating bands; pulls attendance live from the
    /// Attendance module. Reads employee/department data but never writes payroll.
    /// </summary>
    public interface IPerformanceService
    {
        // -- reviews -----------------------------------------------------------

        /// <summary>Creates a draft review seeded with the default general criteria (/20).</summary>
        Result<long> CreateDraft(long employeeId, int periodYear, string periodLabel, string reviewer);

        /// <summary>Creates a draft review by snapshotting a template's criteria (versioned).</summary>
        Result<long> CreateFromTemplate(long employeeId, long templateId, int periodYear, string periodLabel,
            string reviewer, long? reviewerEmployeeId, long? cycleId, DateTime? dueDate, bool selfAssessment);

        /// <summary>Saves the review header and its criteria (only while it is a draft).</summary>
        Result Save(PerformanceReview review, IEnumerable<PerformanceCriterion> criteria);

        /// <summary>Finalises a review: all criteria must be scored and a reviewer set.</summary>
        Result Complete(long id);

        /// <summary>Reopens a completed review for correction.</summary>
        Result Reopen(long id);

        /// <summary>Soft-deletes a review and its criteria.</summary>
        Result Delete(long id);

        PerformanceReview Get(long id);

        PerformanceDetail GetDetail(long id);

        IReadOnlyList<PerformanceSummary> GetByEmployee(long employeeId);

        IReadOnlyList<PerformanceSummary> GetByCompanyYear(long companyId, int year);

        /// <summary>The rating band label for a score on the given scale (default /20).</summary>
        string Rate(decimal score);

        /// <summary>The rating band for a score normalised to its scale.</summary>
        string RateScaled(decimal score, decimal scaleMax);

        // -- templates ---------------------------------------------------------

        IReadOnlyList<TemplateSummary> GetTemplates(long companyId);

        TemplateDetail GetTemplateDetail(long templateId);

        /// <summary>Copies a template into a new, editable company-owned template group.</summary>
        Result<long> DuplicateTemplate(long sourceTemplateId, long companyId, string newName);

        /// <summary>
        /// Saves a company template and its criteria. If the group has already been used by
        /// a review, a NEW version is created (the old one is preserved for past reviews).
        /// Weights must sum to 100.
        /// </summary>
        Result<long> SaveTemplate(PerformanceTemplate template, IEnumerable<PerformanceTemplateCriterion> criteria);

        Result ArchiveTemplate(long templateId);

        Result DeleteTemplate(long templateId);

        // -- department defaults ----------------------------------------------

        IReadOnlyList<PerformanceDeptSetting> GetDeptSettings(long companyId);

        Result SaveDeptSetting(long companyId, string department, string templateGroupKey, long? reviewerEmployeeId);

        // -- cycles ------------------------------------------------------------

        /// <summary>
        /// Launches a cycle and bulk-assigns one review per included employee, each seeded
        /// from its department's default template (or the request fallback), with the
        /// department's default reviewer and the cycle deadline as the due date.
        /// </summary>
        Result<long> LaunchCycle(CycleLaunchRequest request);

        IReadOnlyList<CycleSummary> GetCycles(long companyId);

        CycleDetail GetCycleDetail(long cycleId);

        /// <summary>Recomputes and persists the cycle status from its reviews' completion.</summary>
        Result RefreshCycleStatus(long cycleId);

        Result CancelCycle(long cycleId);

        Result DeleteCycle(long cycleId);

        /// <summary>Pending/overdue reviews for the Notifications engine.</summary>
        IReadOnlyList<PerformanceReminder> GetReminders(long companyId, DateTime asOf);

        // -- goals -------------------------------------------------------------

        IReadOnlyList<GoalRow> GetGoals(long employeeId);

        IReadOnlyList<GoalRow> GetCompanyGoals(long companyId);

        Result<long> CreateGoal(PerformanceGoal goal);

        Result UpdateGoal(PerformanceGoal goal);

        Result SetGoalProgress(long goalId, decimal progressPercent);

        Result SetGoalStatus(long goalId, Enums.PerformanceGoalStatus status);

        Result DeleteGoal(long goalId);

        IReadOnlyList<PerformanceGoalTemplate> GetGoalTemplates(long companyId);

        Result<long> CreateGoalTemplate(PerformanceGoalTemplate template);

        Result DeleteGoalTemplate(long templateId);

        // -- career events -----------------------------------------------------

        Result<long> LogPromotion(long employeeId, string oldPosition, string newPosition, DateTime date, string reason, long? linkedReviewId);

        Result<long> LogReward(long employeeId, decimal amount, string category, DateTime date, string reason);

        Result DeleteCareerEvent(long id);

        CareerTimeline GetCareerTimeline(long employeeId);

        // -- calibration / dashboard / comparison -----------------------------

        CalibrationView GetCalibration(long companyId, int year);

        PerformanceDashboard GetDashboard(long companyId);

        EmployeeComparison Compare(IReadOnlyList<long> employeeIds);
    }
}
