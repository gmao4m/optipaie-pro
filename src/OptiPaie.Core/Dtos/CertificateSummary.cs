using System;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>An issued certificate with the employee name (from the shared record).</summary>
    public sealed class CertificateSummary
    {
        public long CertificateId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public CertificateType Type { get; set; }
        public string Reference { get; set; }
        public DateTime IssueDate { get; set; }
        public string Purpose { get; set; }
    }

    /// <summary>
    /// Everything needed to render a certificate, assembled LIVE from the shared employee
    /// and company records at print time — never stored on the certificate.
    /// </summary>
    public sealed class CertificateRenderModel
    {
        public WorkCertificate Certificate { get; set; }
        public Employee Employee { get; set; }
        public Company Company { get; set; }

        /// <summary>The employee's current monthly base salary (shared record).</summary>
        public decimal MonthlySalary { get; set; }

        /// <summary>Seniority in whole years and months, computed at the issue date.</summary>
        public int SeniorityYears { get; set; }
        public int SeniorityMonths { get; set; }
    }
}
