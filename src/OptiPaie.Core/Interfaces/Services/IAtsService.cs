using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Recruitment (ATS) operations. Manages job postings and their candidate pipeline,
    /// and — the ecosystem link — creates the SHARED employee record when a candidate is
    /// hired, so the new hire flows straight into contracts and payroll.
    /// </summary>
    public interface IAtsService
    {
        Result<long> SavePosting(JobPosting posting);

        Result SetPostingStatus(long postingId, Core.Enums.JobStatus status);

        Result DeletePosting(long postingId);

        JobPosting GetPosting(long postingId);

        JobPostingSummary GetPostingSummary(long postingId);

        IReadOnlyList<JobPostingSummary> GetPostingsByCompany(long companyId);

        // -- candidates --------------------------------------------------------

        Result<long> SaveCandidate(Candidate candidate);

        /// <summary>Moves a candidate to a new pipeline stage (Hired/Rejected use their own methods).</summary>
        Result MoveStage(long candidateId, Core.Enums.CandidateStage stage);

        /// <summary>Marks a candidate rejected.</summary>
        Result Reject(long candidateId);

        /// <summary>
        /// Hires a candidate: creates the SHARED employee for the posting's company,
        /// links it to the candidate, and fills the posting when its positions are met.
        /// </summary>
        Result<HireResult> Hire(long candidateId);

        Result DeleteCandidate(long candidateId);

        Candidate GetCandidate(long candidateId);

        IReadOnlyList<Candidate> GetCandidates(long postingId);
    }
}
