using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;

namespace OptiPaie.PayrollEngine.ElementCalculation
{
    /// <summary>Fixed amount: the amount is the value entered.</summary>
    public sealed class FixedAmountStrategy : ICalculationMethodStrategy
    {
        public CalculationMethod Method => CalculationMethod.FixedAmount;

        public decimal Calculate(PayrollElementInput input, decimal resolvedBase)
        {
            return input.Amount ?? 0m;
        }
    }
}
