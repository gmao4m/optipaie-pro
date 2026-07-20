using NUnit.Framework;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine.Legal;
using OptiPaie.PayrollEngine.Money;
using OptiPaie.PayrollEngine.Pipeline;
using OptiPaie.PayrollEngine.Rules;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Demonstrates that individual rules are independently testable, exercising the
    /// internal pipeline directly (via InternalsVisibleTo).
    /// </summary>
    [TestFixture]
    public sealed class PayrollRuleTests
    {
        private static PayrollCalculationContext NewContext()
        {
            var money = new MoneyEngine(RoundingPolicy.Centime);
            var irg = new IrgCalculator(money);
            LegalProfile profile = new BuiltInLegalProfileProvider().GetProfile(new PayrollPeriod(2026, 6));
            return new PayrollCalculationContext(PayrollTestFactory.Context(0m), profile, money, irg);
        }

        [Test]
        public void CnasRule_Computes_Both_Shares_From_The_Snapshot()
        {
            PayrollCalculationContext ctx = NewContext();
            ctx.CotisableBase = 100000m;

            new CnasRule().Apply(ctx);

            Assert.That(ctx.CnasEmployee, Is.EqualTo(9000m));
            Assert.That(ctx.CnasEmployer, Is.EqualTo(26000m));
        }

        [Test]
        public void GrossSalaryRule_Adds_Gains_And_Subtracts_Deductions_In_Gross()
        {
            PayrollCalculationContext ctx = NewContext();
            ctx.Lines.Add(new WorkingLine { ElementType = ElementType.Gain, Amount = 50000m, IsIncludedInGross = true });
            ctx.Lines.Add(new WorkingLine { ElementType = ElementType.Gain, Amount = 5000m, IsIncludedInGross = true });
            ctx.Lines.Add(new WorkingLine { ElementType = ElementType.Deduction, Amount = 2000m, IsIncludedInGross = true });
            ctx.Lines.Add(new WorkingLine { ElementType = ElementType.Deduction, Amount = 9999m, IsIncludedInGross = false });

            new GrossSalaryRule().Apply(ctx);

            Assert.That(ctx.GrossSalary, Is.EqualTo(53000m));
        }

        [Test]
        public void CotisableBaseRule_Sums_Only_Cnas_Applicable_Lines()
        {
            PayrollCalculationContext ctx = NewContext();
            ctx.Lines.Add(new WorkingLine { ElementType = ElementType.Gain, Amount = 50000m, IsCnasApplicable = true });
            ctx.Lines.Add(new WorkingLine { ElementType = ElementType.Gain, Amount = 5000m, IsCnasApplicable = false });

            new CotisableBaseRule().Apply(ctx);

            Assert.That(ctx.CotisableBase, Is.EqualTo(50000m));
        }
    }
}
