using System.Collections.Generic;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Property/invariant tests run across a wide range of salaries. These assert
    /// relationships that must always hold (rather than precomputed magic numbers),
    /// giving broad correctness coverage and exceeding the required scenario count.
    /// </summary>
    [TestFixture]
    public sealed class PayrollEngineInvariantTests
    {
        private const decimal CnasEmployeeRate = 0.09m;

        public static IEnumerable<decimal> Bases()
        {
            for (decimal value = 0m; value <= 400000m; value += 2500m)
            {
                yield return value;
            }
        }

        private static PayrollResult Run(decimal baseSalary)
        {
            return new PayrollCalculationEngine().Calculate(PayrollTestFactory.Context(baseSalary));
        }

        [TestCaseSource(nameof(Bases))]
        public void Calculation_Succeeds_For_Any_NonNegative_Salary(decimal baseSalary)
        {
            Assert.That(Run(baseSalary).IsSuccess, Is.True);
        }

        [TestCaseSource(nameof(Bases))]
        public void Net_Equals_Gross_Minus_Cnas_Minus_Irg(decimal baseSalary)
        {
            PayrollResult r = Run(baseSalary);
            decimal expected = r.Totals.SalaireBrut - r.Totals.CnasEmployee - r.Totals.Irg;
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(Bases))]
        public void Cnas_Employee_Equals_CotisableBase_Times_Rate(decimal baseSalary)
        {
            PayrollResult r = Run(baseSalary);
            decimal expected = RoundingPolicy.Centime.Round(r.Totals.BaseCotisable * CnasEmployeeRate);
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(Bases))]
        public void Gross_Equals_Base_When_No_Elements(decimal baseSalary)
        {
            Assert.That(Run(baseSalary).Totals.SalaireBrut, Is.EqualTo(baseSalary));
        }

        [TestCaseSource(nameof(Bases))]
        public void Calculation_Is_Deterministic(decimal baseSalary)
        {
            PayrollResult a = Run(baseSalary);
            PayrollResult b = Run(baseSalary);
            Assert.That(b.Totals.NetSalaire, Is.EqualTo(a.Totals.NetSalaire));
            Assert.That(b.Totals.Irg, Is.EqualTo(a.Totals.Irg));
            Assert.That(b.Totals.CnasEmployee, Is.EqualTo(a.Totals.CnasEmployee));
        }

        [TestCaseSource(nameof(Bases))]
        public void Exemption_Holds_Below_Threshold(decimal baseSalary)
        {
            PayrollResult r = Run(baseSalary);
            if (r.Totals.BaseImposable <= 30000m)
            {
                Assert.That(r.Totals.Irg, Is.EqualTo(0m));
            }
        }

        [TestCaseSource(nameof(Bases))]
        public void Irg_Never_Exceeds_Taxable_Base(decimal baseSalary)
        {
            PayrollResult r = Run(baseSalary);
            Assert.That(r.Totals.Irg, Is.LessThanOrEqualTo(r.Totals.BaseImposable));
            Assert.That(r.Totals.Irg, Is.GreaterThanOrEqualTo(0m));
        }
    }
}
