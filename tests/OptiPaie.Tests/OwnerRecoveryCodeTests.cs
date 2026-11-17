using System;
using System.Data.SQLite;
using System.IO;
using NUnit.Framework;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Licensing;
using OptiPaie.Services.Startup;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The owner recovery code, proven by a test rather than a human watching a screen:
    ///   • on an ADOPTED-OWNER database (an account that predates the recovery feature, so it has no
    ///     code), the one-time code is issued on first sign-in and is idempotent thereafter;
    ///   • using the code ROTATES the secret — the old password stops working and the new one works —
    ///     and rotates the CODE — the used code is consumed and a fresh one takes its place.
    /// </summary>
    [TestFixture]
    public sealed class OwnerRecoveryCodeTests
    {
        private string _dir;
        private UnitOfWorkFactory _uow;
        private SettingsService _settings;
        private CustomerAccountService _account;
        private CompanyService _companies;
        private OwnerSetupService _setup;

        [SetUp]
        public void SetUp()
        {
            SqliteTypeHandlers.Register();
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "s.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
            _settings = new SettingsService(_uow);
            _account = new CustomerAccountService(_settings, new PassthroughCipher(), NullLogger.Instance);
            _companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            _setup = new OwnerSetupService(_settings, _account, _companies, NullLogger.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void RecoveryCode_IsIssued_OnFirstSignIn_OfAnAdoptedOwnerDatabase()
        {
            // An account that predates the recovery feature: it exists, but carries no recovery code.
            _account.Register("owner@sarl.dz", "SARL", "ownerPass-123");
            Assert.That(_account.HasAccount, Is.True);
            Assert.That(_account.HasRecoveryCode, Is.False, "a legacy/adopted account has no code yet");

            _setup.AdoptExistingAccount(); // first launch adopts the existing owner in place

            string code = _setup.BackfillRecoveryCodeIfMissing(); // issued on that first sign-in
            Assert.That(code, Is.Not.Null.And.Not.Empty, "the one-time code is issued for the adopted owner");
            Assert.That(_account.HasRecoveryCode, Is.True);

            // Idempotent: it is not re-issued once present.
            Assert.That(_setup.BackfillRecoveryCodeIfMissing(), Is.Null, "an existing code is never re-issued");
        }

        [Test]
        public void UsingTheRecoveryCode_RotatesBothTheSecretAndTheCode()
        {
            _account.Register("owner@sarl.dz", "SARL", "pass-ONE-1");
            string code1 = _account.IssueRecoveryCode();
            Assert.That(_account.SignIn("owner@sarl.dz", "pass-ONE-1"), Is.True);

            // Use the code → new password, and a FRESH code is returned (the used one is consumed).
            RecoveryResult r1 = _account.RecoverWithCode(code1, "pass-TWO-2");
            Assert.That(r1.Success, Is.True);
            Assert.That(r1.NewRecoveryCode, Is.Not.Null.And.Not.Empty);
            Assert.That(r1.NewRecoveryCode, Is.Not.EqualTo(code1), "the code rotated");

            // The secret rotated: the old password no longer works, the new one does.
            Assert.That(_account.SignIn("owner@sarl.dz", "pass-ONE-1"), Is.False, "old password no longer valid");
            Assert.That(_account.SignIn("owner@sarl.dz", "pass-TWO-2"), Is.True, "new password works");

            // The rotated code works for a further recovery (proves it really replaced the old one).
            RecoveryResult r2 = _account.RecoverWithCode(r1.NewRecoveryCode, "pass-THREE-3");
            Assert.That(r2.Success, Is.True, "the fresh code recovers again");
            Assert.That(_account.SignIn("owner@sarl.dz", "pass-THREE-3"), Is.True);
            Assert.That(_account.SignIn("owner@sarl.dz", "pass-TWO-2"), Is.False);

            // The ORIGINAL code was consumed at the first use and can never be used again.
            RecoveryResult reused = _account.RecoverWithCode(code1, "pass-FOUR-4");
            Assert.That(reused.Success, Is.False, "a consumed code is rejected");
            Assert.That(_account.SignIn("owner@sarl.dz", "pass-THREE-3"), Is.True, "and the password was not changed by the rejected attempt");
        }

        private sealed class PassthroughCipher : ILocalCipher
        {
            public string Protect(string plaintext) => plaintext;
            public string Unprotect(string ciphertext) => ciphertext;
        }
    }
}
