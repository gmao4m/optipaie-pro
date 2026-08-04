using System;
using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for the Performance (Évaluation) module: templates and their criteria,
    /// evaluation periods, evaluations and their scored lines, and the behaviour log.
    /// Company-scoped queries join the shared Employees/Companies tables — no employee,
    /// company or payroll data is ever copied here.
    /// </summary>
    public interface IPerformanceRepository
    {
        // -- templates ---------------------------------------------------------

        EvalTemplate GetTemplate(long id);

        /// <summary>Built-in templates (CompanyId null) plus a company's own, non-deleted.</summary>
        IEnumerable<EvalTemplate> GetTemplatesForCompany(long companyId);

        long InsertTemplate(EvalTemplate template);

        void UpdateTemplate(EvalTemplate template);

        void SoftDeleteTemplate(long id);

        /// <summary>Clears IsDefault on all of a company's templates (before marking a new default).</summary>
        void ClearDefaultTemplate(long companyId);

        // -- criteria ----------------------------------------------------------

        IEnumerable<EvalCriterion> GetCriteria(long templateId);

        long InsertCriterion(EvalCriterion criterion);

        /// <summary>Hard-deletes all criteria of a template (a save replaces the set wholesale).</summary>
        void DeleteCriteria(long templateId);

        // -- periods -----------------------------------------------------------

        EvalPeriod GetPeriod(long id);

        IEnumerable<EvalPeriod> GetPeriodsByCompany(long companyId);

        long InsertPeriod(EvalPeriod period);

        void UpdatePeriod(EvalPeriod period);

        void SoftDeletePeriod(long id);

        // -- evaluations -------------------------------------------------------

        Evaluation GetEvaluation(long id);

        IEnumerable<Evaluation> GetEvaluationsByPeriod(long periodId);

        /// <summary>An employee's evaluations across all periods, most recent period first.</summary>
        IEnumerable<Evaluation> GetEvaluationsByEmployee(long employeeId);

        /// <summary>Every non-deleted evaluation of a company (joins EvalPeriods by company).</summary>
        IEnumerable<Evaluation> GetEvaluationsByCompany(long companyId);

        /// <summary>The evaluation of one employee in one period, if it exists.</summary>
        Evaluation GetForEmployeeInPeriod(long periodId, long employeeId);

        long InsertEvaluation(Evaluation evaluation);

        void UpdateEvaluation(Evaluation evaluation);

        void SoftDeleteEvaluation(long id);

        // -- scored lines ------------------------------------------------------

        IEnumerable<EvaluationScore> GetScores(long evaluationId);

        long InsertScore(EvaluationScore score);

        /// <summary>Hard-deletes all scored lines of an evaluation (replaced wholesale on save).</summary>
        void DeleteScores(long evaluationId);

        // -- behaviour log -----------------------------------------------------

        BehaviorLog GetBehavior(long id);

        /// <summary>An employee's behaviour entries, most recent first.</summary>
        IEnumerable<BehaviorLog> GetBehaviorsByEmployee(long employeeId);

        /// <summary>A company's behaviour entries (joins Employees), most recent first.</summary>
        IEnumerable<BehaviorLog> GetBehaviorsByCompany(long companyId);

        /// <summary>An employee's behaviour entries whose OccurredAt falls in [from, to].</summary>
        IEnumerable<BehaviorLog> GetBehaviorsInRange(long employeeId, DateTime from, DateTime to);

        long InsertBehavior(BehaviorLog behavior);

        void SoftDeleteBehavior(long id);
    }
}
