using System;
using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;

namespace OptiPaie.PayrollEngine.ElementCalculation
{
    /// <summary>
    /// Resolves the amount of a payroll element by dispatching to the strategy for
    /// its calculation method. The calculation base (for percentage/rate methods) is
    /// resolved here; in this version only <see cref="CalculationBase.SalaireDeBase"/>
    /// is supported (validated upstream), mapped to the effective base salary.
    /// </summary>
    public sealed class ElementCalculator
    {
        private readonly Dictionary<CalculationMethod, ICalculationMethodStrategy> _strategies;

        public ElementCalculator() : this(DefaultStrategies())
        {
        }

        public ElementCalculator(IEnumerable<ICalculationMethodStrategy> strategies)
        {
            if (strategies == null)
            {
                throw new ArgumentNullException(nameof(strategies));
            }

            _strategies = new Dictionary<CalculationMethod, ICalculationMethodStrategy>();
            foreach (ICalculationMethodStrategy strategy in strategies)
            {
                _strategies[strategy.Method] = strategy;
            }
        }

        /// <summary>The strategies provided by default (one per supported method).</summary>
        public static IEnumerable<ICalculationMethodStrategy> DefaultStrategies()
        {
            return new ICalculationMethodStrategy[]
            {
                new FixedAmountStrategy(),
                new PercentageStrategy(),
                new QuantityUnitPriceStrategy(),
                new BaseRateStrategy()
            };
        }

        /// <summary>True when the calculator can handle the element's method.</summary>
        public bool Supports(CalculationMethod method)
        {
            return _strategies.ContainsKey(method);
        }

        /// <summary>
        /// Computes the element amount using the effective base salary as the resolved
        /// calculation base.
        /// </summary>
        public decimal Calculate(PayrollElementInput input, decimal effectiveBaseSalary)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (!_strategies.TryGetValue(input.CalculationMethod, out ICalculationMethodStrategy strategy))
            {
                throw new NotSupportedException(
                    "Unsupported calculation method: " + input.CalculationMethod +
                    ". This should have been rejected by validation.");
            }

            return strategy.Calculate(input, effectiveBaseSalary);
        }
    }
}
