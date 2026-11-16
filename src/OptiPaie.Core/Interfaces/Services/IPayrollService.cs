using OptiPaie.Core.Dtos;
using OptiPaie.Core.Payroll;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Orchestrates payroll: builds the engine context from stored data, runs the
    /// pure engine, and (for generation) persists the run, payslip and details
    /// atomically. Contains no calculation logic itself.
    /// </summary>
    public interface IPayrollService
    {
        /// <summary>
        /// Calculates a payslip without saving it (for on-screen preview and the
        /// "explain calculation" view).
        /// </summary>
        PayrollResult Preview(PayrollGenerationRequest request);

        /// <summary>
        /// Calculates and persists a payslip, returning the new payslip id. Fails if
        /// validation fails or a payslip already exists for the employee/period.
        /// <para>
        /// If the pre-issue sanity guard raises any finding, generation is BLOCKED
        /// (<see cref="OptiPaie.Core.Constants.PayrollErrorCodes.SanityBlocked"/>) and
        /// nothing is persisted — use <see cref="Generate(PayrollGenerationRequest, bool)"/>
        /// with an explicit acknowledgement to proceed.
        /// </para>
        /// </summary>
        Result<long> Generate(PayrollGenerationRequest request);

        /// <summary>
        /// As <see cref="Generate(PayrollGenerationRequest)"/>, but when
        /// <paramref name="acknowledgeWarnings"/> is true the caller has deliberately
        /// confirmed the sanity findings: the payslip is issued and the acknowledgement
        /// (operator + finding codes) is written to the audit log.
        /// </summary>
        Result<long> Generate(PayrollGenerationRequest request, bool acknowledgeWarnings);

        /// <summary>
        /// Runs the pre-issue sanity guard on the payslip that WOULD be generated for this
        /// request, without persisting anything. The UI calls this before issuing so it can
        /// show the bilingual confirmation. An empty report means the payslip is clean.
        /// </summary>
        PayslipSanityReport Inspect(PayrollGenerationRequest request);
    }
}
