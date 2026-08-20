using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A candidate for a <see cref="JobPosting"/>. A candidate is NOT an employee — the
    /// data lives only in the recruitment module. When hired, the module creates the
    /// SHARED employee record and links it here through <see cref="HiredEmployeeId"/>.
    /// </summary>
    public sealed class Candidate : EntityBase
    {
        public long PostingId { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Phone { get; set; }

        public string Email { get; set; }

        public CandidateStage Stage { get; set; }

        /// <summary>Rating out of 5 (0 = not rated).</summary>
        public int Rating { get; set; }

        /// <summary>Where the candidate came from (site, cooptation, …).</summary>
        public string Source { get; set; }

        public string Notes { get; set; }

        public DateTime AppliedDate { get; set; }

        // -- v1.29 recruitment fields (additive, all optional) ----------------

        /// <summary>Highest education level (free text, optional).</summary>
        public string EducationLevel { get; set; }

        /// <summary>Years of experience (optional).</summary>
        public int? ExperienceYears { get; set; }

        /// <summary>
        /// When Stage = Rejected, tells a refusal (by us) apart from a withdrawal (by the
        /// candidate); null when the file is still open. The CHECK on Stage is never touched.
        /// </summary>
        public CandidateClosure? ClosureType { get; set; }

        /// <summary>Mandatory reason captured when the file is closed (refus / désistement).</summary>
        public string ClosureReason { get; set; }

        public DateTime? ClosureDate { get; set; }

        /// <summary>The shared employee created when this candidate was hired, or null.</summary>
        public long? HiredEmployeeId { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
