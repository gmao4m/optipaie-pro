using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Auth;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 3 — security hardening. Proves: the 10-char minimum is enforced, an older
    /// (lower-cost) password hash is transparently upgraded on the next successful login, an
    /// administrator reset forces a password change at next login, and repeated failed logins are
    /// slowed by a growing back-off that a correct login clears.
    /// </summary>
    [TestFixture]
    public sealed class SecurityHardeningTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uowf;
        private IUserService _users;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-sec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uowf = new UnitOfWorkFactory(factory);
            _users = new UserService(_uowf, new SettingsService(_uowf));
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        [Test]
        public void MinimumPasswordLength_Is10()
        {
            Assert.That(UserService.MinPasswordLength, Is.EqualTo(10));

            // 9 chars rejected, 10 chars accepted.
            Result<long> tooShort = _users.Create("u1", "U", "123456789", UserRole.Admin, null);
            Assert.That(tooShort.IsFailure, Is.True);
            Assert.That(tooShort.ErrorCode, Is.EqualTo("User_PasswordTooShort"));

            Assert.That(_users.Create("u2", "U", "1234567890", UserRole.Admin, null).IsSuccess, Is.True);
        }

        [Test]
        public void StoredHash_UsesCurrentCost_AndNeverTheClearPassword()
        {
            long id = _users.Create("u", "U", "averysafepass", UserRole.Admin, null).Value;
            string stored = _users.Get(id).PasswordHash;
            Assert.That(stored, Does.StartWith("600000$"), "new hashes record the 600k cost");
            Assert.That(stored, Does.Not.Contain("averysafepass"));
        }

        [Test]
        public void LegacyHash_IsUpgraded_OnNextSuccessfulLogin()
        {
            // Create a user, then overwrite its stored hash with the OLD bare 100k-iteration form
            // (no "iterations$" prefix) to simulate an account created before this hardening.
            long id = _users.Create("legacy", "L", "averysafepass", UserRole.Admin, null).Value;
            User u = _users.Get(id);
            u.PasswordHash = LegacyBareHash("averysafepass", u.Salt, 100000);
            using (IUnitOfWork uow = _uowf.Create()) uow.Users.Update(u);

            Assert.That(_users.Get(id).PasswordHash, Does.Not.Contain("$"), "precondition: stored as a legacy bare hash");

            // Login with the correct password still works...
            Assert.That(_users.Authenticate("legacy", "averysafepass").IsSuccess, Is.True);

            // ...and the stored hash has been transparently upgraded to the current cost.
            Assert.That(_users.Get(id).PasswordHash, Does.StartWith("600000$"), "the hash was re-hashed at 600k on login");

            // And the account still authenticates after the upgrade.
            Assert.That(_users.Authenticate("legacy", "averysafepass").IsSuccess, Is.True);
        }

        [Test]
        public void AdminReset_ForcesPasswordChange_SelfChangeClearsIt()
        {
            long id = _users.Create("u", "U", "averysafepass", UserRole.Admin, null).Value;
            Assert.That(_users.Get(id).MustChangePassword, Is.False);

            // Admin resets to a temporary password -> must change at next login.
            Assert.That(_users.ChangePassword(id, "temporary01", mustChangePassword: true).IsSuccess, Is.True);
            Assert.That(_users.Get(id).MustChangePassword, Is.True);

            // The user picks their own password -> flag cleared.
            Assert.That(_users.ChangePassword(id, "myownpass01", mustChangePassword: false).IsSuccess, Is.True);
            Assert.That(_users.Get(id).MustChangePassword, Is.False);
        }

        [Test]
        public void LoginThrottle_Grows_WithFailures_AndResets_OnSuccess()
        {
            var t = new LoginThrottle();
            // First two failures cost nothing (honest mistakes).
            Assert.That(t.GetDelay("user"), Is.EqualTo(TimeSpan.Zero));
            t.OnFailure("user");
            t.OnFailure("user");
            Assert.That(t.GetDelay("user"), Is.EqualTo(TimeSpan.Zero));

            // Then the delay appears and grows.
            TimeSpan d3 = t.OnFailure("user");
            TimeSpan d4 = t.OnFailure("user");
            TimeSpan d5 = t.OnFailure("user");
            Assert.That(d3, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(d4, Is.GreaterThan(d3));
            Assert.That(d5, Is.GreaterThan(d4));

            // A success wipes the slate clean.
            t.OnSuccess("user");
            Assert.That(t.GetDelay("user"), Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void LoginCoordinator_AppliesGrowingBackoff_ThenResetsAfterCorrectLogin()
        {
            _users.Create("chef", "Chef", "averysafepass", UserRole.Manager, "Prod");

            var delays = new List<TimeSpan>();
            var coordinator = new LoginCoordinator(null, _users, new LoginThrottle(), d => delays.Add(d));

            // A run of wrong passwords.
            for (int i = 0; i < 5; i++)
            {
                LoginOutcome bad = coordinator.Login("chef", "wrongpass123");
                Assert.That(bad.Success, Is.False);
            }

            // The sleeps that were actually applied (non-zero) must be strictly increasing.
            var applied = delays.FindAll(d => d > TimeSpan.Zero);
            Assert.That(applied.Count, Is.GreaterThan(0), "a back-off must kick in after several failures");
            for (int i = 1; i < applied.Count; i++)
            {
                Assert.That(applied[i], Is.GreaterThan(applied[i - 1]), "the back-off must keep growing");
            }

            // A correct login clears the back-off; a later single failure waits again from zero.
            Assert.That(coordinator.Login("chef", "averysafepass").Success, Is.True);
            delays.Clear();
            coordinator.Login("chef", "wrongpass123");
            Assert.That(delays.TrueForAll(d => d == TimeSpan.Zero), Is.True, "the counter reset after the correct login");
        }

        private static string LegacyBareHash(string password, string saltBase64, int iterations)
        {
            byte[] salt = Convert.FromBase64String(saltBase64);
            using (var k = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return Convert.ToBase64String(k.GetBytes(32));
            }
        }
    }
}
