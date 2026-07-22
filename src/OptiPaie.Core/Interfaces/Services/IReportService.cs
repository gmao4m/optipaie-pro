using System.Collections.Generic;
using OptiPaie.Core.Dtos;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// The Reports Center: a cross-module report library. Read-only — it aggregates the
    /// other modules through their services and never touches the payroll engine.
    /// </summary>
    public interface IReportService
    {
        /// <summary>The available reports (for the library list).</summary>
        IReadOnlyList<ReportDescriptor> GetReports();

        /// <summary>
        /// The available reports for a given company — the general library plus any
        /// company-specific declarations (e.g. the CACOBATPH section for a BTPH employer).
        /// </summary>
        IReadOnlyList<ReportDescriptor> GetReports(long companyId);

        /// <summary>Builds one report for a company and period into a uniform table.</summary>
        ReportTable Build(string reportKey, long companyId, int year, int month);
    }
}
