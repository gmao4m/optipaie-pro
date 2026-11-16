using OptiPaie.Core.Dtos;
using OptiPaie.Core.Payroll;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Inspects a computed payslip for the "silently wrong" patterns that must not be
    /// issued without a deliberate confirmation (PART 2). Pure and side-effect free:
    /// it reads the engine's own <see cref="PayrollResult"/> and returns findings; it
    /// never changes a number and never touches the payroll engine.
    /// </summary>
    public interface IPayslipSanityService
    {
        /// <summary>
        /// Evaluates every sanity rule against a computed result.
        /// </summary>
        /// <param name="result">The engine result for the payslip about to be issued.</param>
        /// <param name="snmg">The SNMG from the legal snapshot (contribution-floor check).</param>
        /// <param name="fullTime">True when the month was worked in full (no proration),
        /// which is the only case in which a below-SNMG base is flagged.</param>
        PayslipSanityReport Inspect(PayrollResult result, decimal snmg, bool fullTime);
    }
}
