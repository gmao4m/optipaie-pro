using System;
using System.Collections.Generic;
using OptiPaie.Core.Certificates;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Bridges OptiPaie employees/companies to the official Algerian CNAS attestations —
    /// ATS (Attestation de Travail et de Salaire) and DRT (Déclaration de Reprise ou de
    /// Non Reprise de Travail). Auto-fills the certificate snapshot from the stored
    /// employee/company files, then runs the verbatim-ported certificate logic and fills
    /// the real official .docx templates by bookmark.
    /// </summary>
    public interface IAtsDrtDocumentService
    {
        /// <summary>Snapshots an OptiPaie company into the certificate model (auto-fill). Null if missing.</summary>
        Company MapCompany(long companyId);

        /// <summary>Snapshots an OptiPaie employee into the certificate model (auto-fill). Null if missing.</summary>
        Employee MapEmployee(long employeeId);

        /// <summary>The 12-row month grid used on the ATS certificate.</summary>
        List<MonthlyContribution> BuildMonthGrid(DateTime startDate, int numberOfMonths, bool arabicMonthNames);

        /// <summary>True when Microsoft Word is installed (needed only for the PDF export step).</summary>
        bool IsWordAvailable { get; }

        /// <summary>
        /// Fills the official ATS_Template.docx with the company/employee + work stoppage +
        /// 12-month contribution grid, saving to <paramref name="outputDocxPath"/>. Returns that path.
        /// </summary>
        string GenerateAts(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, List<MonthlyContribution> contributions, WeekendConfig weekend, string outputDocxPath);

        /// <summary>Fills DRT_Template_AR.docx (arabic=true) or DRT_Template_FR.docx and returns the saved path.</summary>
        string GenerateDrt(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, bool arabic, WeekendConfig weekend, string outputDocxPath);

        /// <summary>Converts a filled .docx to .pdf via Word automation. Throws if Word is unavailable.</summary>
        string ConvertToPdf(string docxPath, string pdfPath);
    }
}
