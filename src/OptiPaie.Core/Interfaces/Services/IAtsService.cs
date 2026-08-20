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

        /// <summary>Strictly-ordered move (one active step forward/back only). Enforced in the service.</summary>
        Result MoveStage(long candidateId, Core.Enums.CandidateStage stage);

        /// <summary>Advance to the immediate next stage — the single "étape suivante" action.</summary>
        Result MoveNext(long candidateId);

        /// <summary>Step back exactly one stage to correct a mistake.</summary>
        Result MoveBack(long candidateId);

        /// <summary>Closes the file as a refusal (motif obligatoire).</summary>
        Result Reject(long candidateId, string reason);

        /// <summary>Closes the file as a withdrawal by the candidate (motif obligatoire).</summary>
        Result Desist(long candidateId, string reason);

        /// <summary>
        /// Hires a candidate (from « Retenu »): in one atomic transaction creates the SHARED
        /// employee (validated + audited), a draft contract, links it to the candidate, and
        /// fills the posting when its positions are met.
        /// </summary>
        Result<HireResult> Hire(long candidateId);

        Result DeleteCandidate(long candidateId);

        Candidate GetCandidate(long candidateId);

        IReadOnlyList<Candidate> GetCandidates(long postingId);

        /// <summary>Every candidate of a COMPANY (company-scoped; throws for companyId &lt;= 0).</summary>
        IReadOnlyList<Candidate> GetCandidatesByCompany(long companyId);

        // -- interviews --------------------------------------------------------

        Result<long> SaveInterview(Interview interview);

        IReadOnlyList<Interview> GetInterviews(long candidateId);

        Result DeleteInterview(long interviewId);

        // -- attachments -------------------------------------------------------

        Result<long> AddAttachment(CandidateAttachment attachment);

        IReadOnlyList<CandidateAttachment> GetAttachments(long candidateId);

        Result DeleteAttachment(long attachmentId);
    }
}
