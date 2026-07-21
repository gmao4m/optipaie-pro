using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for issued certificates. Company-scoped queries join the shared
    /// Employees table — the company is never stored on the certificate.
    /// </summary>
    public interface IWorkCertificateRepository
    {
        WorkCertificate GetById(long id);

        /// <summary>Certificates of one employee, most recent first.</summary>
        IEnumerable<WorkCertificate> GetByEmployee(long employeeId);

        /// <summary>Certificates of a whole company, most recent first.</summary>
        IEnumerable<WorkCertificate> GetByCompany(long companyId);

        /// <summary>Number of certificates issued for a company in a given year (for numbering).</summary>
        int CountForCompanyYear(long companyId, int year);

        long Insert(WorkCertificate certificate);

        void Update(WorkCertificate certificate);

        void SoftDelete(long id);
    }
}
