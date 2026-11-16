using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Payroll;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// PART 2 — the pre-issue sanity guard. Every expected value is hand-built here
    /// (RULE 0): the tests construct a PayrollResult with chosen totals/lines and assert
    /// which findings fire. No product constant is used to build an expectation.
    /// </summary>
    [TestFixture]
    public sealed class PayslipSanityServiceTests
    {
        private readonly PayslipSanityService _sut = new PayslipSanityService();

        private static PayrollTotals Totals(
            decimal gross, decimal cotisable, decimal cnasEmp, decimal taxable, decimal irg, decimal net)
        {
            // order: SalaireBrut, BaseCotisable, CnasEmployee, CnasEmployer, BaseImposable, IrgBrut, Abattement, Irg, NetSalaire
            return new PayrollTotals(gross, cotisable, cnasEmp, 0m, taxable, 0m, 0m, irg, net);
        }

        private static PayrollLineResult Line(string label, ElementType type, decimal amount, bool cnas, bool irg)
        {
            // order: ElementId, LabelFr, LabelAr, ElementType, Base, Rate, Quantity, UnitPrice, Amount, IsCnasApplicable, IsIrgApplicable, DisplayOrder
            return new PayrollLineResult(null, label, label, type, null, null, null, null, amount, cnas, irg, 0);
        }

        private static PayrollResult Result(PayrollTotals totals, params PayrollLineResult[] lines)
        {
            return new PayrollResult(totals, lines.ToList(), null, null, "test", "test", "test", DateTime.UtcNow);
        }

        private List<PayslipSanityCode> Codes(PayrollResult r, decimal snmg = 24000m, bool fullTime = true)
        {
            return _sut.Inspect(r, snmg, fullTime).Warnings.Select(w => w.Code).ToList();
        }

        [Test]
        public void CleanPayslip_HasNoWarnings()
        {
            // base 60000 fully subject: cnas 5400, taxable 54600, irg 7042, net 47558.
            var r = Result(
                Totals(gross: 60000m, cotisable: 60000m, cnasEmp: 5400m, taxable: 54600m, irg: 7042m, net: 47558m),
                Line("Salaire de base", ElementType.Gain, 60000m, cnas: true, irg: true));

            Assert.That(_sut.Inspect(r, 24000m, fullTime: true).HasWarnings, Is.False);
        }

        [Test]
        public void PaidSalaryWithZeroContribution_FlagsRule1And2()
        {
            // The owner's payslip #22 shape: gross 30000, nothing withheld, net = gross.
            var r = Result(
                Totals(gross: 30000m, cotisable: 0m, cnasEmp: 0m, taxable: 0m, irg: 0m, net: 30000m),
                Line("Salaire de base", ElementType.Gain, 30000m, cnas: false, irg: false));

            List<PayslipSanityCode> codes = Codes(r);
            Assert.That(codes, Contains.Item(PayslipSanityCode.ContributionZeroOnPaidSalary));
            Assert.That(codes, Contains.Item(PayslipSanityCode.NetEqualsGross));
        }

        [Test]
        public void DuplicateLineName_FlagsRule3()
        {
            var r = Result(
                Totals(gross: 50000m, cotisable: 50000m, cnasEmp: 4500m, taxable: 45500m, irg: 4585m, net: 40915m),
                Line("Salaire de base", ElementType.Gain, 40000m, cnas: true, irg: true),
                Line("Prime", ElementType.Gain, 5000m, cnas: true, irg: true),
                Line("Prime", ElementType.Gain, 5000m, cnas: true, irg: true));

            List<PayslipSanityCode> codes = Codes(r);
            Assert.That(codes, Contains.Item(PayslipSanityCode.DuplicateLineName));
            Assert.That(codes.Count(c => c == PayslipSanityCode.DuplicateLineName), Is.EqualTo(1), "one finding per duplicated name");
        }

        [Test]
        public void LargeExemptGain_Flags_ButSmallExemptAllowanceDoesNot()
        {
            // 20000 exempt-from-both gain on a 30000 gross = 66% -> flagged.
            var big = Result(
                Totals(gross: 30000m, cotisable: 10000m, cnasEmp: 900m, taxable: 9100m, irg: 0m, net: 29100m),
                Line("Salaire de base", ElementType.Gain, 10000m, cnas: true, irg: true),
                Line("Prime cachée", ElementType.Gain, 20000m, cnas: false, irg: false));
            Assert.That(Codes(big), Contains.Item(PayslipSanityCode.LargeExemptGain));

            // 3000 transport on a 35000 gross = 8.6% -> NOT flagged as a large exempt gain.
            var small = Result(
                Totals(gross: 35000m, cotisable: 32000m, cnasEmp: 2880m, taxable: 29120m, irg: 0m, net: 32120m),
                Line("Salaire de base", ElementType.Gain, 32000m, cnas: true, irg: true),
                Line("Prime de Transport", ElementType.Gain, 3000m, cnas: false, irg: false));
            Assert.That(Codes(small), Does.Not.Contain(PayslipSanityCode.LargeExemptGain));
        }

        [Test]
        public void BelowSnmg_FlagsOnlyWhenFullTime()
        {
            var r = Result(
                Totals(gross: 20000m, cotisable: 20000m, cnasEmp: 1800m, taxable: 18200m, irg: 0m, net: 18200m),
                Line("Salaire de base", ElementType.Gain, 20000m, cnas: true, irg: true));

            Assert.That(_sut.Inspect(r, 24000m, fullTime: true).Warnings.Select(w => w.Code),
                Contains.Item(PayslipSanityCode.BelowSnmgFullTime));
            // Part-time / prorated month: a below-SNMG base is expected, not flagged.
            Assert.That(_sut.Inspect(r, 24000m, fullTime: false).Warnings.Select(w => w.Code),
                Does.Not.Contain(PayslipSanityCode.BelowSnmgFullTime));
        }

        [Test]
        public void EveryWarning_CarriesBothFrenchAndArabic()
        {
            var r = Result(
                Totals(gross: 30000m, cotisable: 0m, cnasEmp: 0m, taxable: 0m, irg: 0m, net: 30000m),
                Line("Salaire de base", ElementType.Gain, 30000m, cnas: false, irg: false));

            foreach (PayslipSanityWarning w in _sut.Inspect(r, 24000m, true).Warnings)
            {
                Assert.That(w.MessageFr, Is.Not.Null.And.Not.Empty, "French message missing for " + w.Code);
                Assert.That(w.MessageAr, Is.Not.Null.And.Not.Empty, "Arabic message missing for " + w.Code);
                // the Arabic string actually contains Arabic letters
                Assert.That(w.MessageAr.Any(ch => ch >= '؀' && ch <= 'ۿ'), Is.True,
                    "Arabic message has no Arabic letters for " + w.Code);
            }
        }
    }
}
