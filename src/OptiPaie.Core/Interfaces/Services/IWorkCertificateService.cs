using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Work-certificate operations. Stores only the issue metadata and assembles the
    /// document content LIVE from the shared employee and company records, so every
    /// certificate reflects the current data.
    /// </summary>
    public interface IWorkCertificateService
    {
        /// <summary>Creates or updates a certificate (auto-numbers a new one).</summary>
        Result<long> Save(WorkCertificate certificate);

        Result Delete(long certificateId);

        WorkCertificate Get(long certificateId);

        /// <summary>Assembles the render model (shared employee + company + salary) for printing.</summary>
        CertificateRenderModel BuildRender(long certificateId);

        /// <summary>Certificates of one employee.</summary>
        IReadOnlyList<CertificateSummary> GetByEmployee(long employeeId);

        /// <summary>Certificates of a whole company.</summary>
        IReadOnlyList<CertificateSummary> GetByCompany(long companyId);
    }
}
