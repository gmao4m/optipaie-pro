using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Tests
{
    /// <summary>
    /// End-to-end engine scenarios with hand-verified expected values, covering the
    /// categories required by the brief (low/medium/high salary, panier, rendement,
    /// exceptional & retroactive bonuses, ancienneté, acompte, absence, net-only
    /// adjustments, different CNAS rates, rounding policy and element configurations).
    /// </summary>
    [TestFixture]
    public sealed class PayrollEngineScenarioTests
    {
        private static PayrollResult Run(PayrollContext context)
        {
            PayrollResult result = new PayrollCalculationEngine().Calculate(context);
            Assert.That(result.IsSuccess, Is.True, "Scenario should succeed.");
            return result;
        }

        [Test]
        public void LowSalary_AtSnmg_IsExemptFromIrg()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(24000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(2160m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(21840m));
            Assert.That(r.Totals.Irg, Is.EqualTo(0m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(21840m));
        }

        [Test]
        public void LowSalary_AtExemptionThreshold_HasNoIrg()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(30000m));
            Assert.That(r.Totals.Irg, Is.EqualTo(0m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(27300m));
        }

        [Test]
        public void SmoothingBand_Base36000()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(36000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(3240m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(32760m));
            Assert.That(r.Totals.Irg, Is.EqualTo(1239.58m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(31520.42m));
        }

        [Test]
        public void Medium_WithRendementAndPanier()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(60000m,
                PayrollTestFactory.Gain("Prime de Rendement", 5000m),
                PayrollTestFactory.NonCotisableGain("Prime de Panier", 4000m)));

            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(69000m));
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(65000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(5850m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(59150m));
            Assert.That(r.Totals.IrgBrut, Is.EqualTo(9770.50m));
            Assert.That(r.Totals.Abattement, Is.EqualTo(1500m));
            Assert.That(r.Totals.Irg, Is.EqualTo(8270.50m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(54879.50m));
        }

        [Test]
        public void Complex_AllElementKinds()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(80000m,
                PayrollTestFactory.Gain("IEP", 8000m),
                PayrollTestFactory.Gain("Prime de Responsabilité", 10000m),
                PayrollTestFactory.Gain("Heures Supplémentaires", 6000m),
                PayrollTestFactory.NonCotisableGain("Prime de Panier", 6000m),
                PayrollTestFactory.NonCotisableGain("Prime de Transport", 4000m),
                PayrollTestFactory.Absence(5000m),
                PayrollTestFactory.NetDeduction("Acompte", 10000m)));

            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(109000m));
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(99000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(8910m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(90090m));
            Assert.That(r.Totals.Irg, Is.EqualTo(16927m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(73163m));
        }

        [Test]
        public void PrimePanier_IsInGrossButNotCotisableNorTaxable()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(50000m,
                PayrollTestFactory.NonCotisableGain("Prime de Panier", 5000m)));

            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(55000m));
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(50000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(4500m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(45500m));
            Assert.That(r.Totals.Irg, Is.EqualTo(4585m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(45915m));
        }

        [Test]
        public void PrimeRendement_AsPercentageOfBase()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(50000m,
                PayrollTestFactory.Percentage("Prime de Rendement", 0.10m)));

            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(55000m));
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(55000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(4950m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(50050m));
            Assert.That(r.Totals.Irg, Is.EqualTo(5813.50m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(44236.50m));
        }

        [Test]
        public void PrimeAnciennete_AsBaseRate()
        {
            // base × 2%/year × 5 years = 5000 (same numbers as the 10% rendement case).
            PayrollElementInput iep = PayrollTestFactory.Percentage("IEP", 0m);
            iep.CalculationMethod = OptiPaie.Core.Enums.CalculationMethod.BaseRate;
            iep.Rate = 0.02m;
            iep.Quantity = 5m;

            PayrollResult r = Run(PayrollTestFactory.Context(50000m, iep));

            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(55000m));
            Assert.That(r.Totals.Irg, Is.EqualTo(5813.50m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(44236.50m));
        }

        [Test]
        public void ExceptionalBonus_TaxedInMonth_WhenNotLissed()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(40000m,
                PayrollTestFactory.Gain("Prime Exceptionnelle", 20000m)));

            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(60000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(5400m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(54600m));
            Assert.That(r.Totals.Irg, Is.EqualTo(7042m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(47558m));
        }

        [Test]
        public void RetroactiveBonus_RappelIsLissedOverThreeMonths()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(50000m,
                PayrollTestFactory.Lissaged("Rappel", 30000m, 3, new[] { 28000m, 28000m, 28000m })));

            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(80000m));
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(80000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(7200m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(42800m));
            Assert.That(r.Totals.Irg, Is.EqualTo(11776m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(61024m));
        }

        [Test]
        public void Acompte_ReducesNetOnly()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(50000m,
                PayrollTestFactory.NetDeduction("Acompte", 10000m)));

            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(50000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(4500m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(45500m));
            Assert.That(r.Totals.Irg, Is.EqualTo(4585m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(30915m));
        }

        [Test]
        public void Absence_ReducesAllBases()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(50000m,
                PayrollTestFactory.Absence(10000m)));

            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(40000m));
            Assert.That(r.Totals.BaseCotisable, Is.EqualTo(40000m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(3600m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(36400m));
            Assert.That(r.Totals.Irg, Is.EqualTo(2272m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(34128m));
        }

        [Test]
        public void ZeroSalary_ProducesAllZeros()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(0m));
            Assert.That(r.Totals.SalaireBrut, Is.EqualTo(0m));
            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(0m));
            Assert.That(r.Totals.Irg, Is.EqualTo(0m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(0m));
        }

        [Test]
        public void DifferentCnasRate_IsApplied()
        {
            LegalSnapshot snapshot = PayrollTestFactory.Snapshot(0.10m, 0.26m, 24000m, RoundingPolicy.Centime);
            PayrollResult r = Run(PayrollTestFactory.Context(50000m, snapshot));

            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(5000m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(45000m));
            Assert.That(r.Totals.Irg, Is.EqualTo(4450m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(40550m));
        }

        [Test]
        public void WholeDinarRoundingPolicy_RoundsToInteger()
        {
            LegalSnapshot snapshot = PayrollTestFactory.Snapshot(0.09m, 0.26m, 24000m, RoundingPolicy.WholeDinar);
            PayrollResult r = Run(PayrollTestFactory.Context(33333m, snapshot));

            Assert.That(r.Totals.CnasEmployee, Is.EqualTo(3000m));
            Assert.That(r.Totals.BaseImposable, Is.EqualTo(30333m));
            Assert.That(r.Totals.Irg, Is.EqualTo(207m));
            Assert.That(r.Totals.NetSalaire, Is.EqualTo(30126m));
        }

        [Test]
        public void ElementConfiguration_CnasFlagChangesResult()
        {
            PayrollResult cotisable = Run(PayrollTestFactory.Context(50000m,
                PayrollTestFactory.Gain("Prime", 10000m, cnas: true, irg: true)));
            PayrollResult notCotisable = Run(PayrollTestFactory.Context(50000m,
                PayrollTestFactory.Gain("Prime", 10000m, cnas: false, irg: true)));

            Assert.That(cotisable.Totals.BaseCotisable, Is.EqualTo(60000m));
            Assert.That(notCotisable.Totals.BaseCotisable, Is.EqualTo(50000m));
            Assert.That(notCotisable.Totals.CnasEmployee, Is.LessThan(cotisable.Totals.CnasEmployee));
        }

        [Test]
        public void ExplainTrace_ContainsEveryStage()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(60000m));
            string[] keys = { "BASE", "GROSS", "COTISABLE", "CNAS", "TAXABLE", "IRG_BRUT", "ABATTEMENT", "IRG_REGULAR", "IRG_TOTAL", "NET_DEDUCTIONS", "NET" };
            foreach (string key in keys)
            {
                Assert.That(r.Trace, Has.Some.Matches<PayrollCalculationStep>(s => s.Key == key), "Trace must contain " + key);
            }
        }

        [Test]
        public void AuditMetadata_IsStamped()
        {
            PayrollResult r = Run(PayrollTestFactory.Context(60000m));
            Assert.That(r.EngineVersion, Is.EqualTo(EngineVersion.Version));
            Assert.That(r.LegalVersion, Is.EqualTo("DZ-2026"));
            Assert.That(r.CalculationVersion, Is.EqualTo(EngineVersion.CalculationVersion));
        }
    }
}
