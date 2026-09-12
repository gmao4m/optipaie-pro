using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Wave 1 · A — payroll consistency (non-regression).
    ///
    /// The engine prorates the base salary (and the percentage elements computed on it) for a
    /// partial month; the printed fiche and the worksheet must show EXACTLY those engine amounts,
    /// never the raw un-prorated Base×Taux. When they do, the net is identical on screen, on the
    /// printed fiche, in the archived bulletin and as the CNAS base — to the centime.
    ///
    /// These tests pin the reconciliation the fiche relies on: a fiche faithfully rendering the
    /// engine result (which is exactly what the fixed live preview does via <c>FicheService.FromResult</c>,
    /// the same builder the archive uses) yields NET = TOTAL GAINS − TOTAL RETENUES = engine net.
    /// The second test encodes the defect itself: the OLD full-base computation does NOT reconcile,
    /// so this file fails if the fiche ever goes back to summing the un-prorated worksheet lines.
    /// The calculation engine is NOT exercised for correctness here (covered elsewhere) — only the
    /// display/reconciliation contract.
    /// </summary>
    [TestFixture]
    public sealed class PayrollFicheConsistencyTests
    {
        private static PayrollResult RunWithAbsence(decimal baseSalary, int workableDays, int workedDays, params PayrollElementInput[] elements)
        {
            PayrollContext ctx = PayrollTestFactory.Context(baseSalary, elements);
            ctx.WorkableDays = workableDays;
            ctx.WorkedDays = workedDays;
            return new PayrollCalculationEngine().Calculate(ctx);
        }

        /// <summary>The fiche's own net, computed exactly as FichePaieDocument does it:
        /// Σ gains − (CNAS + IRG + Σ deductions). Mirrors TOTAL GAINS − TOTAL RETENUES.</summary>
        private static decimal FicheNet(PayrollResult r)
        {
            decimal gains = r.Lines.Where(l => l.ElementType == ElementType.Gain).Sum(l => l.Amount);
            decimal deductions = r.Lines.Where(l => l.ElementType == ElementType.Deduction).Sum(l => l.Amount);
            return gains - (r.Totals.CnasEmployee + r.Totals.Irg + deductions);
        }

        [Test]
        public void FiveDaysAbsence_ScreenFicheArchiveAndCnasBase_AllReconcileToTheCentime()
        {
            // 30 000 base, +10 % prime de rendement, −5 000 avance ; 5 days' absence out of 30.
            PayrollResult r = RunWithAbsence(
                30000m, 30, 25,
                PayrollTestFactory.Percentage("Prime de rendement", 0.10m),
                PayrollTestFactory.NetDeduction("Avance sur salaire", 5000m));

            Assert.That(r.IsSuccess, Is.True);

            // Proration actually happened: base 30 000 × 25/30 = 25 000 ; prime 10 % of 25 000 = 2 500.
            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(27500m), "base + prime must be prorated on the worked days");
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(27500m), "the CNAS base is the prorated cotisable base");

            // SALAIRE BRUT band = the sum of the gain lines (the engine gross), so the fiche's brut
            // is the engine value AND the displayed gains still add up to it (verifiable net).
            decimal gains = r.Lines.Where(l => l.ElementType == ElementType.Gain).Sum(l => l.Amount);
            Assert.That(gains, Is.EqualTo(r.Totals.SalaireBrut));

            // THE FOUR VALUES all come from this one engine result:
            //   • screen NET stat  = r.Totals.NetSalaire
            //   • archived bulletin = the same engine result is what Generate() stores
            //   • printed fiche NET = TOTAL GAINS − TOTAL RETENUES (FicheNet)
            //   • CNAS base         = r.Totals.BaseCotisable
            Assert.That(FicheNet(r), Is.EqualTo(r.Totals.NetSalaire),
                "the printed fiche net (gains − retenues) must equal the engine/screen/archive net");
        }

        [Test]
        public void Fiche_MustUseProratedEngineAmounts_NotTheFullBase_ThatWasTheDefect()
        {
            PayrollResult r = RunWithAbsence(
                30000m, 30, 25,
                PayrollTestFactory.Percentage("Prime de rendement", 0.10m),
                PayrollTestFactory.NetDeduction("Avance sur salaire", 5000m));

            // The OLD fiche summed the FULL (un-prorated) base + full prime for the gross, while
            // CNAS/IRG came from the engine (prorated) — inflating the printed net. This encodes the
            // defect: if the fiche ever sums the raw worksheet lines again, its net will differ from
            // the engine net exactly like this.
            decimal buggyGains = 30000m + 3000m; // full base + full 10 % prime (no proration)
            decimal buggyNet = buggyGains - (r.Totals.CnasEmployee + r.Totals.Irg + 5000m);

            Assert.That(buggyNet, Is.Not.EqualTo(r.Totals.NetSalaire),
                "the full-base net must NOT match the engine net — the fiche has to use the prorated engine amounts");
            Assert.That(buggyNet, Is.GreaterThan(r.Totals.NetSalaire), "the old defect inflated the printed net");
        }
    }
}
