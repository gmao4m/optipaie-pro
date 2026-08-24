using System.Globalization;
using System.Threading;
using NUnit.Framework;
using OptiPaie.Common.Text;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The whole point of the fix: numeric input accepts a comma OR a dot as the decimal separator,
    /// regardless of the Windows regional configuration, and never mangles a valid amount.
    /// </summary>
    [TestFixture]
    public sealed class FlexibleNumberTests
    {
        [TestCase("26,5", 26.5)]
        [TestCase("26.5", 26.5)]
        [TestCase("10,5", 10.5)]
        [TestCase("10.5", 10.5)]
        [TestCase("0", 0)]
        [TestCase("0,00", 0)]
        [TestCase("1250,50", 1250.50)]
        [TestCase("1250.50", 1250.50)]
        [TestCase("1250", 1250)]
        [TestCase("0,5", 0.5)]
        [TestCase(".5", 0.5)]
        [TestCase(",5", 0.5)]
        [TestCase("10%", null)]      // '%' is not a number — the caller (Taux) strips it, not this parser
        [TestCase("  1250,50  ", 1250.50)]
        [TestCase("1 250,50", 1250.50)]     // space grouping
        [TestCase("1 250,50", 1250.50)] // non-breaking-space grouping
        [TestCase("-42,75", -42.75)]
        [TestCase("-42.75", -42.75)]
        [TestCase("1,250.50", 1250.50)]     // English grouping + dot decimal
        [TestCase("1.250,50", 1250.50)]     // French grouping + comma decimal
        [TestCase("1.000.000", 1000000)]    // dot thousands grouping, no decimal
        [TestCase("1,000,000", 1000000)]    // comma thousands grouping, no decimal
        [TestCase("", null)]
        [TestCase("   ", null)]
        [TestCase(null, null)]
        [TestCase("abc", null)]
        public void TryParse_AcceptsBothSeparators_AnyCulture(string input, double? expected)
        {
            // Prove it is culture-independent: run the exact same asserts under a dot-culture and a comma-culture.
            foreach (var cultureName in new[] { "en-US", "fr-FR", "ar-DZ" })
            {
                var prev = Thread.CurrentThread.CurrentCulture;
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                try
                {
                    bool ok = FlexibleNumber.TryParse(input, out decimal value);
                    if (expected == null)
                    {
                        // "10%" contains a digit but a trailing '%' → not a plain number here.
                        Assert.That(ok, Is.False, $"[{cultureName}] '{input}' should not parse as a bare number");
                    }
                    else
                    {
                        Assert.That(ok, Is.True, $"[{cultureName}] '{input}' should parse");
                        Assert.That(value, Is.EqualTo((decimal)expected.Value), $"[{cultureName}] '{input}'");
                    }
                }
                finally { Thread.CurrentThread.CurrentCulture = prev; }
            }
        }

        [Test]
        public void PayrollBaseTimesRate_CommaEqualsDot_ToTheCentime()
        {
            // Client case: Base 10,5 × Taux 1000 = 10 500,00 ; and 26,5 jours == 26.5 jours.
            Assert.That(FlexibleNumber.TryParse("10,5", out decimal baseComma), Is.True);
            Assert.That(FlexibleNumber.TryParse("10.5", out decimal baseDot), Is.True);
            Assert.That(baseComma, Is.EqualTo(baseDot));
            Assert.That(baseComma * 1000m, Is.EqualTo(10500m));

            FlexibleNumber.TryParse("26,5", out decimal jComma);
            FlexibleNumber.TryParse("26.5", out decimal jDot);
            Assert.That(jComma, Is.EqualTo(jDot).And.EqualTo(26.5m));
        }

        [Test]
        public void CommaAndDot_ProduceIdenticalResult_ToTheCentime()
        {
            Assert.That(FlexibleNumber.TryParse("34567,89", out decimal a), Is.True);
            Assert.That(FlexibleNumber.TryParse("34567.89", out decimal b), Is.True);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.EqualTo(34567.89m));
        }

        [Test]
        public void Double_Overload_Works()
        {
            Assert.That(FlexibleNumber.TryParse("3,25", out double d), Is.True);
            Assert.That(d, Is.EqualTo(3.25d));
        }
    }
}
