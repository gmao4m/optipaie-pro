using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;

namespace OptiPaie.PayrollEngine.ElementCalculation
{
    /// <summary>Percentage: amount = resolved base × rate (rate as a fraction, e.g. 0.10).</summary>
    public sealed class PercentageStrategy : ICalculationMethodStrategy
    {
        public CalculationMethod Method => CalculationMethod.Percentage;

        public decimal Calculate(PayrollElementInput input, decimal resolvedBase)
        {
            return resolvedBase * (input.Rate ?? 0m);
        }
    }
}
