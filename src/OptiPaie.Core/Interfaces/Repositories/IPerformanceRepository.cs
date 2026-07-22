using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for the whole Performance &amp; Career module: reviews and their criteria,
    /// versioned templates, review cycles, goals, career events and per-department defaults.
    /// Company-scoped queries join the shared Employees/Companies tables — no employee,
    /// company or payroll data is ever copied here.
    /// </summary>
    public interface IPerformanceRepository
    {
        // -- reviews -----------------------------------------------------------

        PerformanceReview GetById(long id);

        /// <summary>Reviews of one employee, most recent first.</summary>
        IEnumerable<PerformanceReview> GetByEmployee(long employeeId);

        /// <summary>Reviews of a whole company for a year.</summary>
        IEnumerable<PerformanceReview> GetByCompanyYear(long companyId, int year);

        /// <summary>Every non-deleted review of a company across all years (dashboard/search).</summary>
        IEnumerable<PerformanceReview> GetByCompany(long companyId);

        /// <summary>Reviews assigned to a cycle.</summary>
        IEnumerable<PerformanceReview> GetByCycle(long cycleId);

        long Insert(PerformanceReview review);

        void Update(PerformanceReview review);

        void SoftDelete(long id);

        // -- criteria ----------------------------------------------------------

        /// <summary>Criteria of one review, in display order.</summary>
        IEnumerable<PerformanceCriterion> GetCriteria(long reviewId);

        long InsertCriterion(PerformanceCriterion criterion);

        void UpdateCriterion(PerformanceCriterion criterion);

        /// <summary>Hard-deletes a criterion (they are only ever child rows of a draft).</summary>
        void DeleteCriterion(long id);

        // -- templates ---------------------------------------------------------

        PerformanceTemplate GetTemplate(long id);

        /// <summary>Current, non-archived templates visible to a company (built-ins + its own).</summary>
        IEnumerable<PerformanceTemplate> GetTemplatesForCompany(long companyId);

        /// <summary>The current version of a template group (optionally company-scoped).</summary>
        PerformanceTemplate GetCurrentTemplateByGroup(string groupKey, long? companyId);

        /// <summary>Every version of a group, newest first (version history).</summary>
        IEnumerable<PerformanceTemplate> GetTemplateVersions(string groupKey);

        long InsertTemplate(PerformanceTemplate template);

        void UpdateTemplate(PerformanceTemplate template);

        /// <summary>Clears IsCurrent for every current row of a group (before adding a new version).</summary>
        void SupersedeTemplateGroup(string groupKey, long? companyId);

        void SoftDeleteTemplate(long id);

        /// <summary>True if any review was created from a template in this group.</summary>
        bool IsTemplateGroupUsed(string groupKey);

        IEnumerable<PerformanceTemplateCriterion> GetTemplateCriteria(long templateId);

        long InsertTemplateCriterion(PerformanceTemplateCriterion criterion);

        /// <summary>Hard-deletes all criteria of a template (used to replace an unused template's set).</summary>
        void DeleteTemplateCriteria(long templateId);

        // -- cycles ------------------------------------------------------------

        PerformanceCycle GetCycle(long id);

        IEnumerable<PerformanceCycle> GetCyclesByCompany(long companyId);

        long InsertCycle(PerformanceCycle cycle);

        void UpdateCycle(PerformanceCycle cycle);

        void SoftDeleteCycle(long id);

        // -- goals -------------------------------------------------------------

        PerformanceGoal GetGoal(long id);

        IEnumerable<PerformanceGoal> GetGoalsByEmployee(long employeeId);

        IEnumerable<PerformanceGoal> GetGoalsByCompany(long companyId);

        long InsertGoal(PerformanceGoal goal);

        void UpdateGoal(PerformanceGoal goal);

        void SoftDeleteGoal(long id);

        IEnumerable<PerformanceGoalTemplate> GetGoalTemplates(long companyId);

        long InsertGoalTemplate(PerformanceGoalTemplate template);

        void SoftDeleteGoalTemplate(long id);

        // -- career events -----------------------------------------------------

        IEnumerable<PerformanceCareerEvent> GetCareerEventsByEmployee(long employeeId);

        /// <summary>Career events of a whole company (joins the shared Employees table).</summary>
        IEnumerable<PerformanceCareerEvent> GetCareerEventsByCompany(long companyId);

        long InsertCareerEvent(PerformanceCareerEvent careerEvent);

        void SoftDeleteCareerEvent(long id);

        // -- department defaults ----------------------------------------------

        IEnumerable<PerformanceDeptSetting> GetDeptSettings(long companyId);

        PerformanceDeptSetting GetDeptSetting(long companyId, string department);

        long InsertDeptSetting(PerformanceDeptSetting setting);

        void UpdateDeptSetting(PerformanceDeptSetting setting);
    }
}
