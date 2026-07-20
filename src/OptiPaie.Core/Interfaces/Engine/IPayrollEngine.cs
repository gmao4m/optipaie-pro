using OptiPaie.Core.Dtos;

namespace OptiPaie.Core.Interfaces.Engine
{
    /// <summary>
    /// The payroll calculation engine. Pure and deterministic: it takes a fully
    /// self-contained <see cref="PayrollContext"/> and returns a complete
    /// <see cref="PayrollResult"/>. It performs no I/O and depends on nothing
    /// outside the domain model.
    /// </summary>
    public interface IPayrollEngine
    {
        /// <summary>
        /// Validates, calculates, verifies and returns the payroll result for one
        /// employee/period. Never throws for business problems — those are reported
        /// as error messages on the result with <see cref="PayrollResult.IsSuccess"/> false.
        /// </summary>
        PayrollResult Calculate(PayrollContext context);
    }
}
