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

        /// <summary>The shared employee created when this candidate was hired, or null.</summary>
        public long? HiredEmployeeId { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
