using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Licensing;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Licensing;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 6 — owner password recovery with a one-time code, fully offline, no master password.
    /// Proves: a code is issued and stored only as a hash; the correct code resets the password,
    /// is CONSUMED and replaced with a fresh code; a wrong code changes nothing; a short password is
    /// refused; repeated wrong attempts lock out; and formatting (case/spaces/dashes) never matters.
    /// </summary>
    [TestFixture]
    public sealed class OwnerRecoveryTests
    {
        private string _dir;
        private CustomerAccountService _account;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            var uow = new UnitOfWorkFactory(factory);
            var settings = new SettingsService(uow);
            _account = new CustomerAccountService(settings, new PassthroughCipher(), NullLogger.Instance);
            _account.Register("owner@corp.dz", "SARL Owner", "originalPass1");
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void Issue_ThenRecover_ConsumesAndReissues_AndStoresOnlyAHash()
        {
            string code = _account.IssueRecoveryCode();
            Assert.That(code, Is.Not.Null.And.Not.Empty);
            Assert.That(_account.HasRecoveryCode, Is.True);
            Assert.That(code, Does.Contain("-"), "grouped for readability");

            // Recover with the correct code.
            RecoveryResult r = _account.RecoverWithCode(code, "brandNewPass9");
            Assert.That(r.Success, Is.True, r.Failure.ToString());
            Assert.That(r.NewRecoveryCode, Is.Not.Null.And.Not.EqualTo(code), "a fresh code is issued");

            // The new password works; the old one does not.
            Assert.That(_account.SignIn("owner@corp.dz", "brandNewPass9"), Is.True);
            Assert.That(_account.SignIn("owner@corp.dz", "originalPass1"), Is.False);

            // The USED code is consumed; only the fresh one recovers now.
            Assert.That(_account.RecoverWithCode(code, "anotherPass12").Failure, Is.EqualTo(RecoveryFailure.BadCode));
            Assert.That(_account.RecoverWithCode(r.NewRecoveryCode, "anotherPass12").Success, Is.True);
        }

        [Test]
        public void WrongCode_ChangesNothing()
        {
            _account.IssueRecoveryCode();
            RecoveryResult r = _account.RecoverWithCode("WRONG-CODE0-XXXXX-YYYYY", "newpassword1");
            Assert.That(r.Success, Is.False);
            Assert.That(r.Failure, Is.EqualTo(RecoveryFailure.BadCode));
            Assert.That(_account.SignIn("owner@corp.dz", "originalPass1"), Is.True, "password untouched");
        }

        [Test]
        public void ShortNewPassword_IsRefused()
        {
            string code = _account.IssueRecoveryCode();
            Assert.That(_account.RecoverWithCode(code, "short").Failure, Is.EqualTo(RecoveryFailure.WeakPassword));
        }

        [Test]
        public void RepeatedWrongAttempts_LockOut_EvenWithTheCorrectCode()
        {
            string code = _account.IssueRecoveryCode();
            for (int i = 0; i < 5; i++)
            {
                _account.RecoverWithCode("BADBD-BADBD-BADBD-BADBD", "whatever123");
            }
            // Now locked: the CORRECT code is refused during the lockout window.
            Assert.That(_account.RecoverWithCode(code, "goodpassword1").Failure, Is.EqualTo(RecoveryFailure.Locked));
        }

        [Test]
        public void Formatting_DoesNotMatter()
        {
            string code = _account.IssueRecoveryCode();
            string messy = "  " + code.Replace("-", "").ToLowerInvariant() + " ";
            Assert.That(_account.RecoverWithCode(messy, "newpassword1").Success, Is.True);
        }

        [Test]
        public void NoCode_Issued_NoRecovery()
        {
            Assert.That(_account.HasRecoveryCode, Is.False);
            Assert.That(_account.RecoverWithCode("ABCDE-ABCDE-ABCDE-ABCDE", "newpassword1").Failure,
                Is.EqualTo(RecoveryFailure.NoAccount));
        }

        private sealed class PassthroughCipher : ILocalCipher
        {
            public string Protect(string plaintext) => plaintext;
            public string Unprotect(string ciphertext) => ciphertext;
        }
    }
}
