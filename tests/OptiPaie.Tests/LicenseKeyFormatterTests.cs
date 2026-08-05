using NUnit.Framework;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The client license-key format must match what the backend actually issues
    /// (gen_license_key): a product prefix + three groups of four, e.g. PAY-XXXX-XXXX-XXXX.
    /// A mismatch means the activation button never enables and the real, printed keys are
    /// silently refused — so this pins the format to the batch keys clients receive.
    /// </summary>
    [TestFixture]
    public sealed class LicenseKeyFormatterTests
    {
        // A real key from backend/admin/register-batch-30.sql.
        private const string RealKey = "PAY-G9FP-DMPZ-DJXS";

        [Test]
        public void Format_InsertsDashesAfterPrefixAndGroups()
        {
            Assert.That(LicenseKeyFormatter.Format("PAYG9FPDMPZDJXS"), Is.EqualTo(RealKey));
        }

        [Test]
        public void Format_IsIdempotentOnAnAlreadyFormattedKey()
        {
            Assert.That(LicenseKeyFormatter.Format(RealKey), Is.EqualTo(RealKey));
        }

        [Test]
        public void IsComplete_TrueForARealKey_FalseForAPartial()
        {
            Assert.That(LicenseKeyFormatter.IsComplete(RealKey), Is.True);
            Assert.That(LicenseKeyFormatter.IsComplete("PAY-G9FP-DMPZ"), Is.False);
            Assert.That(LicenseKeyFormatter.IsComplete(""), Is.False);
        }

        [Test]
        public void Canonical_UppercasesAndFormats_EmptyWhenIncomplete()
        {
            Assert.That(LicenseKeyFormatter.Canonical("pay-g9fp-dmpz-djxs"), Is.EqualTo(RealKey));
            Assert.That(LicenseKeyFormatter.Canonical("PAY-G9FP"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void RawLength_Is15()
        {
            Assert.That(LicenseKeyFormatter.RawLength, Is.EqualTo(15));
        }

        [Test]
        public void FormattedCompleteKey_Is18Chars()
        {
            // 15 alphanumerics + 3 dashes. The activation TextBox MaxLength MUST be >= 18,
            // otherwise a complete key can never be typed and activation is impossible.
            Assert.That(RealKey.Length, Is.EqualTo(18));
            Assert.That(LicenseKeyFormatter.Format("PAYG9FPDMPZDJXS").Length, Is.EqualTo(18));
        }
    }
}
