using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for performance reviews and their criteria. Company-scoped queries
    /// join the shared Employees table — the company is never stored on the review.
    /// </summary>
    public interface IPerformanceRepository
    {
        PerformanceReview GetById(long id);

        /// <summary>Reviews of one employee, most recent first.</summary>
        IEnumerable<PerformanceReview> GetByEmployee(long employeeId);

        /// <summary>Reviews of a whole company for a year.</summary>
        IEnumerable<PerformanceReview> GetByCompanyYear(long companyId, int year);

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
    }
}
