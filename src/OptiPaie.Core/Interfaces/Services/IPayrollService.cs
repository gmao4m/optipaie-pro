using OptiPaie.Core.Dtos;
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
        /// </summary>
        Result<long> Generate(PayrollGenerationRequest request);
    }
}
