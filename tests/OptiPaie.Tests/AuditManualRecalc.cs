using System.Globalization;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Tests
{
    /// <summary>
    /// E2E audit — S14. Prints the ENGINE output for three deliberately different payslips so the
    /// auditor can compare it, dinar-by-dinar, against an INDEPENDENT hand calculation done from the
    /// official LF-2022 barème (not from any recorded program value). [Explicit] — run on demand:
    ///   dotnet test --filter "FullyQualifiedName~AuditManualRecalc" -c Debug
    /// </summary>
    [TestFixture, Explicit]
    public sealed class AuditManualRecalc
    {
        private static readonly PayrollCalculationEngine Engine = new PayrollCalculationEngine();

        private static void Print(string name, PayrollContext ctx)
        {
            PayrollResult r = Engine.Calculate(ctx);
            Assert.That(r.IsSuccess, Is.True, name + " must calculate");
            PayrollTotals t = r.Totals;
            string F(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
            TestContext.Out.WriteLine("=== " + name + " ===");
            TestContext.Out.WriteLine("SalaireBrut   = " + F(t.SalaireBrut));
            TestContext.Out.WriteLine("BaseCotisable = " + F(t.BaseCotisable));
            TestContext.Out.WriteLine("CnasEmployee  = " + F(t.CnasEmployee));
            TestContext.Out.WriteLine("CnasEmployer  = " + F(t.CnasEmployer));
            TestContext.Out.WriteLine("BaseImposable = " + F(t.BaseImposable));
            TestContext.Out.WriteLine("IrgBrut       = " + F(t.IrgBrut));
            TestContext.Out.WriteLine("Abattement    = " + F(t.Abattement));
            TestContext.Out.WriteLine("Irg           = " + F(t.Irg));
            TestContext.Out.WriteLine("NetSalaire    = " + F(t.NetSalaire));
            TestContext.Out.WriteLine("");
        }

        [Test]
        public void Print_Three_Payslips_For_Manual_Comparison()
        {
            // Case A — base salary only, no allowances.
            Print("A: base 50000, no elements",
                PayrollTestFactory.Context(50000m));

            // Case B — cotisable % allowance + non-cotisable allowance + net acompte deduction.
            Print("B: base 40000 + rendement 10% + transport 3000 (non-cotisable) + acompte 5000",
                PayrollTestFactory.Context(40000m,
                    PayrollTestFactory.Percentage("Prime de Rendement", 0.10m),
                    PayrollTestFactory.NonCotisableGain("Prime de Transport", 3000m),
                    PayrollTestFactory.NetDeduction("Acompte", 5000m)));

            // Case C — low-income smoothing band (taxable base in 30000..35000).
            Print("C: base 36000, no elements (smoothing band)",
                PayrollTestFactory.Context(36000m));
        }
    }
}
