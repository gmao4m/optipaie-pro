using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;

namespace OptiPaie.PayrollEngine.ElementCalculation
{
    /// <summary>
    /// Base × rate × quantity (e.g. ancienneté = salaire de base × rate-per-year ×
    /// years). Quantity defaults to 1 when not supplied.
    /// </summary>
    public sealed class BaseRateStrategy : ICalculationMethodStrategy
    {
        public CalculationMethod Method => CalculationMethod.BaseRate;

        public decimal Calculate(PayrollElementInput input, decimal resolvedBase)
        {
            decimal quantity = input.Quantity ?? 1m;
            return resolvedBase * (input.Rate ?? 0m) * quantity;
        }
    }
}
