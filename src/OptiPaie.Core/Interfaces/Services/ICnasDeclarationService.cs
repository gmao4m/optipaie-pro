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
    }
}
