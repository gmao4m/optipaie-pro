using System;
using System.Collections.Generic;
using NUnit.Framework;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Services.Licensing;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The local customer account that drives login / logout after the first activation.
    /// Proves: register opens a session; the session survives a "close / reopen"; a manual
    /// logout keeps the account but requires email + password to return; the password is
    /// never stored in clear; and everything works with no network.
    /// </summary>
    [TestFixture]
    public sealed class CustomerAccountServiceTests
    {
        private static CustomerAccountService New(FakeSettings settings) =>
            new CustomerAccountService(settings, new PassThroughCipher(), new NullTestLogger());

        [Test]
        public void Register_OpensSessionAndStoresIdentity()
        {
            var settings = new FakeSettings();
            var account = New(settings);

            account.Register("owner@atlas.dz", "Atlas Industrie", "S3cret!");

            Assert.That(account.HasAccount, Is.True);
            Assert.That(account.IsSignedIn, Is.True, "registration signs the customer in");
            Assert.That(account.Email, Is.EqualTo("owner@atlas.dz"));
            Assert.That(account.Company, Is.EqualTo("Atlas Industrie"));
        }

        [Test]
        public void Session_SurvivesReopen_AutoLogin()
        {
            var settings = new FakeSettings();
            New(settings).Register("owner@atlas.dz", "Atlas", "S3cret!");

            // A brand-new service instance over the SAME store = closing and reopening the app.
            var reopened = New(settings);

            Assert.That(reopened.HasAccount, Is.True);
            Assert.That(reopened.IsSignedIn, Is.True, "the customer stays logged in automatically");
        }

        [Test]
        public void SignOut_KeepsAccount_ButRequiresSignInToReturn()
        {
            var settings = new FakeSettings();
            var account = New(settings);
            account.Register("owner@atlas.dz", "Atlas", "S3cret!");

            account.SignOut();

            Assert.That(account.HasAccount, Is.True, "logout keeps the account (and the license)");
            Assert.That(account.IsSignedIn, Is.False, "but the session is closed");

            // Next launch (fresh instance) is still signed out until email + password are given.
            var reopened = New(settings);
            Assert.That(reopened.IsSignedIn, Is.False);
        }

        [Test]
        public void SignIn_CorrectPassword_ReopensSession()
        {
            var settings = new FakeSettings();
            var account = New(settings);
            account.Register("owner@atlas.dz", "Atlas", "S3cret!");
            account.SignOut();

            Assert.That(account.SignIn("owner@atlas.dz", "S3cret!"), Is.True);
            Assert.That(account.IsSignedIn, Is.True);
        }

        [Test]
        public void SignIn_IsEmailCaseInsensitive()
        {
            var settings = new FakeSettings();
            var account = New(settings);
            account.Register("Owner@Atlas.dz", "Atlas", "S3cret!");
            account.SignOut();

            Assert.That(account.SignIn("owner@atlas.DZ", "S3cret!"), Is.True);
        }

        [Test]
        public void SignIn_WrongPassword_Fails()
        {
            var settings = new FakeSettings();
            var account = New(settings);
            account.Register("owner@atlas.dz", "Atlas", "S3cret!");
            account.SignOut();

            Assert.That(account.SignIn("owner@atlas.dz", "wrong"), Is.False);
            Assert.That(account.IsSignedIn, Is.False);
        }

        [Test]
        public void SignIn_UnknownEmail_Fails()
        {
            var settings = new FakeSettings();
            var account = New(settings);
            account.Register("owner@atlas.dz", "Atlas", "S3cret!");
            account.SignOut();

            Assert.That(account.SignIn("someone@else.dz", "S3cret!"), Is.False);
        }

        [Test]
        public void Password_IsNeverStoredInClear()
        {
            var settings = new FakeSettings();
            New(settings).Register("owner@atlas.dz", "Atlas", "PlainTextP@ss1");

            foreach (string value in settings.Values)
            {
                Assert.That(value, Does.Not.Contain("PlainTextP@ss1"),
                    "the raw password must never appear in the stored settings");
            }
        }

        [Test]
        public void Clear_RemovesTheAccount()
        {
            var settings = new FakeSettings();
            var account = New(settings);
            account.Register("owner@atlas.dz", "Atlas", "S3cret!");

            account.Clear();

            Assert.That(account.HasAccount, Is.False);
            Assert.That(account.IsSignedIn, Is.False);
        }

        // ------------------------------------------------------------ test doubles

        /// <summary>In-memory key/value settings store (only Get/Set are exercised here).</summary>
        private sealed class FakeSettings : ISettingsService
        {
            private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

            public IEnumerable<string> Values => _map.Values;

            public string Get(string key, string defaultValue = null) =>
                _map.TryGetValue(key, out string v) ? v : defaultValue;

            public void Set(string key, string value) => _map[key] = value;

            public string GetLanguage() => "fr";
            public void SetLanguage(string code) { }
            public string GetTheme() => "light";
            public void SetTheme(string theme) { }
            public long? GetDefaultCompanyId() => null;
            public void SetDefaultCompanyId(long? companyId) { }
            public decimal GetOvertimeMajoration() => 0.5m;
        }

        private sealed class PassThroughCipher : ILocalCipher
        {
            public string Protect(string plainText) => plainText;
            public string Unprotect(string protectedBase64) => protectedBase64;
        }

        private sealed class NullTestLogger : OptiPaie.Common.Logging.ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }
}
