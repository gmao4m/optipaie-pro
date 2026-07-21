using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for job postings and candidates. Postings are company-scoped;
    /// candidates live only in this module until hired (then a shared employee is made).
    /// </summary>
    public interface IAtsRepository
    {
        JobPosting GetPostingById(long id);

        IEnumerable<JobPosting> GetPostingsByCompany(long companyId);

        long InsertPosting(JobPosting posting);

        void UpdatePosting(JobPosting posting);

        void SoftDeletePosting(long id);

        // -- candidates --------------------------------------------------------

        Candidate GetCandidateById(long id);

        IEnumerable<Candidate> GetCandidatesByPosting(long postingId);

        long InsertCandidate(Candidate candidate);

        void UpdateCandidate(Candidate candidate);

        void SoftDeleteCandidate(long id);
    }
}
