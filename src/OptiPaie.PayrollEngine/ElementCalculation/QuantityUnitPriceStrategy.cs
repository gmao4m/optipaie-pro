using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;

namespace OptiPaie.PayrollEngine.ElementCalculation
{
    /// <summary>Quantity × unit price (e.g. overtime hours × hourly rate).</summary>
    public sealed class QuantityUnitPriceStrategy : ICalculationMethodStrategy
    {
        public CalculationMethod Method => CalculationMethod.QuantityUnitPrice;

        public decimal Calculate(PayrollElementInput input, decimal resolvedBase)
        {
            return (input.Quantity ?? 0m) * (input.UnitPrice ?? 0m);
        }
    }
}
