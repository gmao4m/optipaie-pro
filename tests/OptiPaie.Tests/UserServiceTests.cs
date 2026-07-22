using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Local user accounts &amp; the optional login gate — integration tests against a real
    /// SQLite file. Prove PBKDF2 hashing (clear password never stored), authentication, the
    /// last-admin guard, and that login stays dormant until enabled with a user present.
    /// </summary>
    [TestFixture]
    public sealed class UserServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uowf;
        private ISettingsService _settings;
        private IUserService _users;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-users-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uowf = new UnitOfWorkFactory(factory);
            _settings = new SettingsService(_uowf);
            _users = new UserService(_uowf, _settings);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        [Test]
        public void Create_HashesThePassword_AndAuthenticates()
        {
            Result<long> created = _users.Create("drh", "Nadia Z.", "secret123", UserRole.Admin, null);
            Assert.That(created.IsSuccess, Is.True, created.Error);

            User stored = _users.Get(created.Value);
            Assert.That(stored.PasswordHash, Is.Not.EqualTo("secret123"), "the clear password is never stored");
            Assert.That(stored.PasswordHash, Is.Not.Empty);
            Assert.That(stored.Salt, Is.Not.Empty);

            Assert.That(_users.Authenticate("drh", "secret123").IsSuccess, Is.True);
            Assert.That(_users.Authenticate("drh", "wrong").IsFailure, Is.True);
            Assert.That(_users.Authenticate("unknown", "secret123").IsFailure, Is.True);
        }

        [Test]
        public void Create_RejectsDuplicateUsername()
        {
            Assert.That(_users.Create("chef", "A", "pass1", UserRole.Manager, "Production").IsSuccess, Is.True);
            Assert.That(_users.Create("chef", "B", "pass2", UserRole.Manager, "Commercial").IsFailure, Is.True);
        }

        [Test]
        public void LastAdmin_CannotBeDeletedOrDemoted()
        {
            long adminId = _users.Create("admin", "Admin", "pass1", UserRole.Admin, null).Value;
            _users.Create("chef", "Chef", "pass2", UserRole.Manager, "Production");

            Assert.That(_users.Delete(adminId).IsFailure, Is.True, "cannot delete the only admin");

            User demote = _users.Get(adminId);
            demote.Role = UserRole.Manager;
            Assert.That(_users.Update(demote).IsFailure, Is.True, "cannot demote the only admin");

            // With a second admin, the first can be removed.
            long admin2 = _users.Create("admin2", "Admin 2", "pass3", UserRole.Admin, null).Value;
            Assert.That(admin2, Is.GreaterThan(0));
            Assert.That(_users.Delete(adminId).IsSuccess, Is.True);
        }

        [Test]
        public void Login_IsDormant_UntilEnabledWithAUser()
        {
            Assert.That(_users.IsLoginRequired(), Is.False, "no users, gate off");

            _users.Create("admin", "Admin", "pass1", UserRole.Admin, null);
            Assert.That(_users.IsLoginRequired(), Is.False, "user exists but the gate is still off by default");

            _users.SetLoginEnabled(true);
            Assert.That(_users.IsLoginRequired(), Is.True, "gate on and a user exists");

            _users.SetLoginEnabled(false);
            Assert.That(_users.IsLoginRequired(), Is.False);
        }

        [Test]
        public void ChangePassword_Rehashes_AndOldPasswordStopsWorking()
        {
            long id = _users.Create("u", "U", "old12345", UserRole.Manager, null).Value;
            string oldHash = _users.Get(id).PasswordHash;

            Assert.That(_users.ChangePassword(id, "new12345").IsSuccess, Is.True);
            Assert.That(_users.Get(id).PasswordHash, Is.Not.EqualTo(oldHash));
            Assert.That(_users.Authenticate("u", "new12345").IsSuccess, Is.True);
            Assert.That(_users.Authenticate("u", "old12345").IsFailure, Is.True);
        }
    }
}
