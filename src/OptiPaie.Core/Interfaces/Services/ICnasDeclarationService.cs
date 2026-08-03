using OptiPaie.Core.Dtos;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Read-only entry point for the CNAS declarations (DAC/DAS). It reads already-produced,
    /// persisted payroll and identity data — it NEVER runs the payroll engine and never
    /// writes. Every operation is strictly scoped to one explicit company.
    /// </summary>
    public interface ICnasDeclarationService
    {
        /// <summary>
        /// Checks whether the company's data is complete enough to produce a CNAS declaration
        /// for <paramref name="year"/> (NSS, date of birth, CNAS employer number, and assiette
        /// ≥ SNMG). Read-only. Throws when no explicit company is supplied.
        /// </summary>
        CnasReadinessReport CheckReadiness(long companyId, int year);

        /// <summary>
        /// Builds the read-only DAC recap for one company over a period (the given months of
        /// <paramref name="year"/> — one month for a monthly DAC, three for a quarterly one):
        /// assiette cotisable, cotisations at the configured rates, the official branch split
        /// (read-only), and the applied-vs-official gap. Aggregates persisted payslips only —
        /// no engine, no rate change. Throws when no explicit company is supplied.
        /// </summary>
        CnasDacReport BuildDac(long companyId, int year, System.Collections.Generic.IReadOnlyList<int> months);

        /// <summary>
        /// Lists the entrées/sorties (hire/exit) that fall inside a DAC period — the movements
        /// annex the accountant copies onto the portal. Reads Employee.HireDate/ExitDate only,
        /// strictly company-scoped. Throws when no explicit company is supplied.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<CnasMovementRow> BuildMovements(
            long companyId, int year, System.Collections.Generic.IReadOnlyList<int> months);
    }
}
