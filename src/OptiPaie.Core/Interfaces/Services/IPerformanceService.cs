using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Performance-review operations. Owns the scoring (weighted average on a /20 scale)
    /// and the rating bands, and pulls the attendance context live from the Attendance
    /// module so a review always reflects the latest presence data.
    /// </summary>
    public interface IPerformanceService
    {
        /// <summary>
        /// Creates a draft review for an employee/period, seeded with the default
        /// criteria, and returns its id.
        /// </summary>
        Result<long> CreateDraft(long employeeId, int periodYear, string periodLabel, string reviewer);

        /// <summary>Saves the review header and its criteria (only while it is a draft).</summary>
        Result Save(PerformanceReview review, IEnumerable<PerformanceCriterion> criteria);

        /// <summary>Finalises a review: all criteria must be scored and a reviewer set.</summary>
        Result Complete(long id);

        /// <summary>Reopens a completed review for correction.</summary>
        Result Reopen(long id);

        /// <summary>Soft-deletes a review and its criteria.</summary>
        Result Delete(long id);

        PerformanceReview Get(long id);

        /// <summary>The full review (criteria + live attendance context) for display/print.</summary>
        PerformanceDetail GetDetail(long id);

        /// <summary>Reviews of one employee with their derived ratings, newest first.</summary>
        IReadOnlyList<PerformanceSummary> GetByEmployee(long employeeId);

        /// <summary>Reviews of a whole company for a year.</summary>
        IReadOnlyList<PerformanceSummary> GetByCompanyYear(long companyId, int year);

        /// <summary>The rating band label for a /20 score.</summary>
        string Rate(decimal score);
    }
}
