using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One interview of a <see cref="Candidate"/>. A candidate may have several. Kept
    /// deliberately minimal: when, what type, by whom, the outcome and a note.
    /// </summary>
    public sealed class Interview : EntityBase
    {
        public long CandidateId { get; set; }

        public DateTime ScheduledDate { get; set; }

        /// <summary>Free-text interview type (téléphonique, présentiel, technique, …).</summary>
        public string Type { get; set; }

        public string Interviewer { get; set; }

        /// <summary>Free-text appraisal / decision.</summary>
        public string Result { get; set; }

        public string Notes { get; set; }

        public bool IsDeleted { get; set; }
    }
}
