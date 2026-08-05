using NUnit.Framework;
using OptiPaie.Services.Licensing;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The customer-facing Supabase Auth client. Covers the pure, offline-testable logic:
    /// deriving the project origin (where /auth/v1 lives) from the configured Edge Functions
    /// base URL, and the "configured" guard the activation window relies on.
    /// </summary>
    [TestFixture]
    public sealed class SupabaseAuthClientTests
    {
        [TestCase("https://ref.supabase.co/functions/v1", "https://ref.supabase.co")]
        [TestCase("https://ref.supabase.co/functions/v1/", "https://ref.supabase.co")]
        [TestCase("https://ref.functions.supabase.co", "https://ref.supabase.co")]
        [TestCase("https://ref.supabase.co", "https://ref.supabase.co")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void DeriveProjectUrl_YieldsTheProjectOrigin(string input, string expected)
        {
            Assert.That(SupabaseAuthClient.DeriveProjectUrl(input), Is.EqualTo(expected));
        }

        [Test]
        public void IsConfigured_RequiresBothUrlAndKey()
        {
            Assert.That(new SupabaseAuthClient("", "key").IsConfigured, Is.False, "no URL");
            Assert.That(new SupabaseAuthClient("https://ref.supabase.co", "").IsConfigured, Is.False, "no key");
            Assert.That(new SupabaseAuthClient("not-a-url", "key").IsConfigured, Is.False, "bad URL");
            Assert.That(new SupabaseAuthClient("https://ref.supabase.co", "sb_publishable_x").IsConfigured, Is.True);
        }
    }
}
