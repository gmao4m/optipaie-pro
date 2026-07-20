using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Provides the configurable payroll legal values and assembles the immutable
    /// <see cref="LegalSnapshot"/> consumed by the payroll engine. This is pure
    /// configuration assembly — it performs NO payroll calculation.
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>The active CNAS employee contribution rate (fraction).</summary>
        decimal GetCnasEmployeeRate();

        /// <summary>The active CNAS employer contribution rate (fraction).</summary>
        decimal GetCnasEmployerRate();

        /// <summary>The active SNMG (dinars).</summary>
        decimal GetSnmg();

        /// <summary>The rounding policy derived from the configured rounding scale.</summary>
        RoundingPolicy GetRoundingPolicy();

        /// <summary>Builds the immutable legal snapshot for a payroll calculation.</summary>
        LegalSnapshot GetLegalSnapshot();
    }
}
