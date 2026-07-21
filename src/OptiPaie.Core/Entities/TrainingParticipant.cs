using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// Enrolment of one SHARED employee in a <see cref="TrainingSession"/>. One row per
    /// (session, employee); the outcome and any certificate reference live here.
    /// </summary>
    public sealed class TrainingParticipant : EntityBase
    {
        public long SessionId { get; set; }

        /// <summary>The shared employee attending.</summary>
        public long EmployeeId { get; set; }

        public TrainingResult Result { get; set; }

        /// <summary>Optional score / mark (free text, e.g. "16/20").</summary>
        public string Score { get; set; }

        /// <summary>Certificate / attestation reference when completed.</summary>
        public string CertificateRef { get; set; }

        public string Notes { get; set; }

        public bool IsDeleted { get; set; }
    }
}
