using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A file attached to a <see cref="Candidate"/> (CV or document). The bytes live on disk
    /// under the recruitment folder; this row only keeps the pointer and a little metadata.
    /// </summary>
    public sealed class CandidateAttachment : EntityBase
    {
        public long CandidateId { get; set; }

        public string FileName { get; set; }

        /// <summary>Path relative to the recruitment root (Recrutement\{CompanyId}\{CandidateId}\).</summary>
        public string RelativePath { get; set; }

        /// <summary>Free-text kind (CV, diplôme, …).</summary>
        public string Kind { get; set; }

        public DateTime AddedAt { get; set; }

        public bool IsDeleted { get; set; }
    }
}
