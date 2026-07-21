using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// An HR document issued for a SHARED employee (attestation de travail, certificat de
    /// travail, attestation de salaire, …). Only the issue metadata is stored; the body
    /// is rendered live from the shared employee and company records, so a certificate
    /// always reflects the current data — nothing is duplicated here.
    /// </summary>
    public sealed class WorkCertificate : EntityBase
    {
        /// <summary>The shared employee the document is for.</summary>
        public long EmployeeId { get; set; }

        public CertificateType Type { get; set; }

        /// <summary>Document reference (auto-generated when left empty).</summary>
        public string Reference { get; set; }

        public DateTime IssueDate { get; set; }

        /// <summary>Purpose / addressee ("pour servir et valoir ce que de droit", a bank, …).</summary>
        public string Purpose { get; set; }

        /// <summary>Free body text, used for a <see cref="CertificateType.Custom"/> document.</summary>
        public string Body { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
