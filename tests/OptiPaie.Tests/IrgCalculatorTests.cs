using System;
using System.Collections.Generic;
using NUnit.Framework;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine.Legal;
using OptiPaie.PayrollEngine.Money;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Verifies the IRG math against hand-computed values from the approved
    /// specification, and proves the calculator reads everything from the profile.
    /// </summary>
    [TestFixture]
    public sealed class IrgCalculatorTests
    {
        private LegalProfile _profile;
        private IrgCalculator _calculator;

        [SetUp]
        public void SetUp()
        {
            _profile = new BuiltInLegalProfileProvider().GetProfile(new PayrollPeriod(2026, 6));
            _calculator = new IrgCalculator(new MoneyEngine(RoundingPolicy.Centime));
        }

        [TestCase(20000, 0)]
        [TestCase(40000, 4600)]
        [TestCase(80000, 15400)]
        [TestCase(160000, 39400)]
        [TestCase(320000, 92200)]
        [TestCase(350000, 102700)]
        public void Bareme_Is_Marginal_And_Progressive(decimal baseImposable, decimal expectedGross)
        {
            decimal gross = _calculator.ComputeBaremePrecise(baseImposable, _profile);
            Assert.That(gross, Is.EqualTo(expectedGross));
        }

        [TestCase(0)]
        [TestCase(15000)]
        [TestCase(21840)]
        [TestCase(30000)]
        public void Income_Up_To_Exemption_Threshold_Is_Tax_Free(decimal baseImposable)
        {
            Assert.That(_calculator.Compute(baseImposable, _profile).Irg, Is.EqualTo(0m));
        }

        [Test]
        public void Abattement_Minimum_Applies_Just_Above_Exemption()
        {
            // base 30001 -> gross 2300.23 -> 40% = 920.09 -> raised to the 1000 minimum.
            Assert.That(_calculator.Compute(30001m, _profile).Abattement, Is.EqualTo(1000m));
        }

        [Test]
        public void Abattement_Maximum_Caps_At_1500()
        {
            Assert.That(_calculator.Compute(59150m, _profile).Abattement, Is.EqualTo(1500m));
        }

        [Test]
        public void Smoothing_Band_Example_32760()
        {
            IrgComputation result = _calculator.Compute(32760m, _profile);
            Assert.That(result.IrgBrut, Is.EqualTo(2934.80m));
            Assert.That(result.Abattement, Is.EqualTo(1173.92m));
            Assert.That(result.Irg, Is.EqualTo(1239.58m));
        }

        [Test]
        public void Normal_Zone_Example_59150()
        {
            IrgComputation result = _calculator.Compute(59150m, _profile);
            Assert.That(result.IrgBrut, Is.EqualTo(9770.50m));
            Assert.That(result.Abattement, Is.EqualTo(1500m));
            Assert.That(result.Irg, Is.EqualTo(8270.50m));
        }

        [Test]
        public void High_Income_Example_350000()
        {
            IrgComputation result = _calculator.Compute(350000m, _profile);
            Assert.That(result.IrgBrut, Is.EqualTo(102700m));
            Assert.That(result.Abattement, Is.EqualTo(1500m));
            Assert.That(result.Irg, Is.EqualTo(101200m));
        }

        [Test]
        public void Smoothing_Is_Continuous_Across_The_Upper_Boundary()
        {
            decimal atBoundary = _calculator.Compute(35000m, _profile).Irg;
            decimal justAbove = _calculator.Compute(35001m, _profile).Irg;
            Assert.That(Math.Abs(justAbove - atBoundary), Is.LessThan(1m));
        }

        [Test]
        public void Irg_Is_Non_Decreasing_In_The_Taxable_Base()
        {
            decimal previous = -1m;
            for (decimal baseImposable = 0m; baseImposable <= 400000m; baseImposable += 2500m)
            {
                decimal irg = _calculator.Compute(baseImposable, _profile).Irg;
                Assert.That(irg, Is.GreaterThanOrEqualTo(previous),
                    "IRG must never decrease as the base grows. Base=" + baseImposable);
                previous = irg;
            }
        }

        [Test]
        public void Calculator_Reads_Brackets_From_The_Profile()
        {
            // A custom profile with completely different rules.
            var custom = new LegalProfile(
                legalVersion: "TEST",
                effectiveFrom: new DateTime(2026, 1, 1),
                irgBrackets: new List<IrgBracket>
                {
                    new IrgBracket(0m, 10000m, 0.00m),
                    new IrgBracket(10000m, null, 0.10m)
                },
                exemptionThreshold: 0m,
                abattement: new AbattementRule(0m, 0m, 0m),
                smoothing: new SmoothingRule(0m, 0m, 1m, 1m, 0m, 1m),
                lissageMethod: LissageMethod.Differential);

            // 20000 -> (20000-10000) * 10% = 1000, no abattement, no smoothing.
            Assert.That(_calculator.Compute(20000m, custom).Irg, Is.EqualTo(1000m));
        }
    }
}
