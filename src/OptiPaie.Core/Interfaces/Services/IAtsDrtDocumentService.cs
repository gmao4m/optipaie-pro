using System;
using System.Collections.Generic;
using OptiPaie.Core.Certificates;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Bridges OptiPaie employees/companies to the official Algerian CNAS attestations —
    /// ATS (Attestation de Travail et de Salaire, AS.08) and DRT (Déclaration de Reprise ou
    /// de Non Reprise de Travail, AS.9). Auto-fills the certificate snapshot from the stored
    /// employee/company files, then prints the values at ABSOLUTE millimetre coordinates so
    /// the output overlays the pre-printed CNAS form exactly (no text flow, no Word).
    /// </summary>
    public interface IAtsDrtDocumentService
    {
        /// <summary>Snapshots an OptiPaie company into the certificate model (auto-fill). Null if missing.</summary>
        Company MapCompany(long companyId);

        /// <summary>Snapshots an OptiPaie employee into the certificate model (auto-fill). Null if missing.</summary>
        Employee MapEmployee(long employeeId);

        /// <summary>The 12-row month grid used on the ATS certificate.</summary>
        List<MonthlyContribution> BuildMonthGrid(DateTime startDate, int numberOfMonths, bool arabicMonthNames);

        /// <summary>
        /// Renders the ATS (AS.08, 2 pages) as a PDF that carries ONLY the values, positioned at
        /// absolute mm so it prints on top of the blank pre-printed form. <paramref name="offsetXmm"/>/
        /// <paramref name="offsetYmm"/> apply the per-printer calibration shift. Returns the PDF path.
        /// </summary>
        string GenerateAts(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, List<MonthlyContribution> contributions, WeekendConfig weekend,
            double offsetXmm, double offsetYmm, string outputPdfPath);

        /// <summary>
        /// Renders the DRT (AS.9, 1 page) as an absolute-coordinate overlay PDF. The official AS.9
        /// is a SINGLE bilingual (French labels + Arabic header) sheet — there is no separate Arabic
        /// form — so the layout is language-independent. Returns the PDF path.
        /// </summary>
        string GenerateDrt(Company company, Employee employee, WorkStoppage stoppage,
            bool hasResumedWork, WeekendConfig weekend,
            double offsetXmm, double offsetYmm, string outputPdfPath);

        /// <summary>
        /// Renders a millimetre calibration target (A4) so the client can superpose it on a blank
        /// form, read how far their printer is off, and enter that as the offset. Returns the PDF path.
        /// </summary>
        string GenerateCalibrationSheet(double offsetXmm, double offsetYmm, string outputPdfPath);
    }
}
