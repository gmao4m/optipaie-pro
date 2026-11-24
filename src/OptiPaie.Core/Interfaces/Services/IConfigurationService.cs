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

        /// <summary>
        /// Whether the legal deductions (IRG + CNAS) are computed on the NEXT payslip. FAIL-SAFE:
        /// a missing row, NULL, unparsable value or any read failure returns TRUE — a failure can
        /// never silently disable the deductions.
        /// </summary>
        bool GetLegalDeductionsEnabled();

        /// <summary>
        /// Switches the legal deductions on/off for future payslips only (saved payslips keep their
        /// own flag). Administrator-only (Permission.ManageSettings); every change is audited
        /// with who, when, and the old → new value.
        /// </summary>
        Result SetLegalDeductionsEnabled(bool enabled);
    }
}
