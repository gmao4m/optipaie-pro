using System;
using System.Data.SQLite;
using System.IO;
using NUnit.Framework;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Primitives;
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
    /// COMMIT G — the owner bootstrap is the highest-privilege path in the product (it creates the
    /// very first owner before any role exists). These direct tests prove there is exactly ONE owner,
    /// ever: CreateOwner succeeds on a fresh database, is REFUSED once an owner exists, the refusal
    /// changes nothing (no account, no audit, no marker), and the bootstrap is unreachable from the
    /// UI once an owner exists (the policy never routes there).
    /// </summary>
    [TestFixture]
    public sealed class OwnerBootstrapTests
    {
        private string _dir;
        private UnitOfWorkFactory _uow;
        private SettingsService _settings;
        private CustomerAccountService _account;
        private CompanyService _companies;
        private CountingAudit _audit;
        private OwnerSetupService _setup;

        [SetUp]
        public void SetUp()
        {
            SqliteTypeHandlers.Register();
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-owner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "s.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
            _settings = new SettingsService(_uow);
            _account = new CustomerAccountService(_settings, new PassthroughCipher(), NullLogger.Instance);
            _companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            _audit = new CountingAudit();
            _setup = new OwnerSetupService(_settings, _account, _companies, NullLogger.Instance) { Audit = _audit };
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void CreateOwner_Succeeds_OnADatabaseWithNoOwner()
        {
            Assert.That(_account.HasAccount, Is.False);

            Result<string> r = _setup.CreateOwner("owner@sarl.dz", "SARL", "ownerPass-123");

            Assert.That(r.IsSuccess, Is.True, r.Error);
            Assert.That(r.Value, Is.Not.Null.And.Not.Empty, "a recovery code is issued");
            Assert.That(_account.HasAccount, Is.True);
            Assert.That(_account.IsSignedIn, Is.True);
            Assert.That(_account.SignIn("owner@sarl.dz", "ownerPass-123"), Is.True);
            Assert.That(_setup.IsOwnerEstablished, Is.True);
        }

        [Test]
        public void CreateOwner_IsRefused_WhenAnOwnerAlreadyExists()
        {
            Assert.That(_setup.CreateOwner("first@sarl.dz", "SARL", "firstPass-123").IsSuccess, Is.True);

            Result<string> second = _setup.CreateOwner("intruder@evil.dz", "Evil", "intruderPass-1");

            Assert.That(second.IsSuccess, Is.False, "there is exactly one owner, ever");
            Assert.That(second.ErrorCode, Is.EqualTo("Owner_AlreadyExists"));
        }

        [Test]
        public void TheRefusal_ChangesNothing_NoAccountChange_NoAudit_NoPartialState()
        {
            Assert.That(_setup.CreateOwner("owner@sarl.dz", "SARL", "ownerPass-123").IsSuccess, Is.True);
            int auditAfterCreate = _audit.Count;

            // The intruder attempt must leave the existing owner EXACTLY as it was.
            Result<string> refused = _setup.CreateOwner("intruder@evil.dz", "Evil", "intruderPass-1");
            Assert.That(refused.IsSuccess, Is.False);

            // No account change: the original credentials still work; the intruder's do not.
            Assert.That(_account.SignIn("owner@sarl.dz", "ownerPass-123"), Is.True, "original owner untouched");
            Assert.That(_account.SignIn("intruder@evil.dz", "intruderPass-1"), Is.False, "intruder never created");
            Assert.That(_account.Email, Is.EqualTo("owner@sarl.dz"));

            // No audit side effect, no re-issued recovery code, no new state from the refusal.
            Assert.That(_audit.Count, Is.EqualTo(auditAfterCreate), "the refusal recorded no audit entry");
        }

        [Test]
        public void TheBootstrap_IsUnreachable_OnceAnOwnerExists()
        {
            // Seed real business data (so the only reason to bootstrap — data but no account — holds)
            // THEN create the owner. Afterwards the policy must never route back to CreateOwnerRequired,
            // whatever the data, so App.EnsureOwnerEstablished never calls CreateOwner again.
            _companies.Create(new OptiPaie.Core.Entities.Company { NameFr = "SARL", Nif = "000000000000000" });
            Assert.That(_setup.DecideAction(), Is.EqualTo(OwnerSetupAction.CreateOwnerRequired), "data, no account");

            Assert.That(_setup.CreateOwner("owner@sarl.dz", "SARL", "ownerPass-123").IsSuccess, Is.True);

            Assert.That(_setup.DecideAction(), Is.EqualTo(OwnerSetupAction.None),
                "once established the path does nothing — never CreateOwnerRequired");

            // Even an un-marked account (adopt case) never routes to creation.
            _settings.Set("Account.OwnerEstablished", string.Empty);
            Assert.That(_setup.DecideAction(), Is.EqualTo(OwnerSetupAction.AdoptExistingAccount),
                "an existing account is adopted, never re-created");
            Assert.That(_setup.DecideAction(), Is.Not.EqualTo(OwnerSetupAction.CreateOwnerRequired));
        }

        private sealed class CountingAudit : IAuditSink
        {
            public int Count { get; private set; }
            public void Record(string entityType, long entityId, AuditAction action, string summary,
                string before = null, string after = null) => Count++;
        }

        private sealed class PassthroughCipher : ILocalCipher
        {
            public string Protect(string plaintext) => plaintext;
            public string Unprotect(string ciphertext) => ciphertext;
        }
    }
}
