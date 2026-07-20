using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;

namespace OptiPaie.PayrollEngine.ElementCalculation
{
    /// <summary>
    /// Computes the amount of a payroll element for one calculation method. New
    /// methods are added as new strategies without modifying existing ones (OCP).
    /// </summary>
    public interface ICalculationMethodStrategy
    {
        /// <summary>The method this strategy handles.</summary>
        CalculationMethod Method { get; }

        /// <summary>
        /// Computes the element amount. <paramref name="resolvedBase"/> is the value of
        /// the element's calculation base (e.g. the effective salaire de base), already
        /// resolved by the caller; methods that do not use a base ignore it.
        /// </summary>
        decimal Calculate(PayrollElementInput input, decimal resolvedBase);
    }
}
