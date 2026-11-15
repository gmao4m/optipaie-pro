using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Enums;
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
    /// COMMIT A — the upgrade-path blocker, proven against a REAL pre-permission database.
    /// <para>
    /// 300 live installs predate the permission system. Their databases are frozen at the last
    /// shipped schema (v36). This test reproduces such a database from the very embedded migration
    /// scripts the product ships, populates it with a company, employees, contracts and payslips —
    /// exactly as a customer's would be — then drives the FULL upgrade a customer's app runs on
    /// first launch: the real <see cref="MigrationRunner"/> (applies 0037..0039) followed by the
    /// real <see cref="OwnerSetupService"/> first-launch path. It asserts the three guarantees the
    /// owner demanded: <b>the data is all still there</b>, <b>the owner can sign in</b>, and <b>every
    /// module the owner should see is reachable</b>.
    /// </para>
    /// Two shapes of pre-permission install are covered:
    /// <list type="bullet">
    /// <item>one that already has a customer account (an activated install) — the account is adopted
    /// in place as OWNER, and its existing password still signs in;</item>
    /// <item>one with data but NO account at all (a very old install) — the owner is created, and the
    /// existing data is untouched.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public sealed class PrePermissionUpgradeTests
    {
        // The last schema version shipped to customers before the permission work (0037..0039 are
        // the unshipped Phase-1 additions). A live customer's DB is recorded at exactly this version.
        private const int PreUpdateVersion = 36;

        private string _directory;
        private string _dbPath;

        [SetUp]
        public void SetUp()
        {
            SqliteTypeHandlers.Register();
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-preperm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _dbPath = Path.Combine(_directory, "customer.db");
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* OS still holds the WAL */ }
        }

        [Test]
        public void ActivatedPreUpdateDatabase_AdoptsAccountAsOwner_DataIntact_OwnerSignsIn_AllModulesReachable()
        {
            // 1. A real activated install, frozen at v36, with live data AND an existing customer
            //    account (email + password) — the common case for the 300 live installs.
            BuildSchemaUpTo(PreUpdateVersion);
            SeedBusinessData();
            RegisterLegacyAccount("owner@sarl.dz", "SARL Ancienne", "ancienMotDePasse1"); // then signed out

            Assert.That(MaxAppliedVersion(), Is.EqualTo(PreUpdateVersion), "fixture is not frozen at v36");
            Assert.That(ColumnExists("Employees", "RowVersion"), Is.False, "not a genuine pre-update DB");

            // 2. Run the exact upgrade the customer's app runs on first launch.
            RunMigrations();
            OwnerSetupService setup = BuildOwnerSetup(out ICustomerAccountService account);

            Assert.That(setup.DecideAction(), Is.EqualTo(OwnerSetupAction.AdoptExistingAccount),
                "an activated install with data must adopt its existing account as owner");
            setup.AdoptExistingAccount();

            // 3a. The owner is established and the path will never run again.
            Assert.That(setup.IsOwnerEstablished, Is.True, "owner setup must be marked complete");
            Assert.That(setup.DecideAction(), Is.EqualTo(OwnerSetupAction.None), "setup must not run twice");

            // 3b. The owner signs in with the password it ALREADY had — never re-created, never reset.
            Assert.That(account.SignIn("owner@sarl.dz", "ancienMotDePasse1"), Is.True,
                "the pre-existing password must still sign the owner in");
            Assert.That(account.SignIn("owner@sarl.dz", "wrong-password"), Is.False);

            // 3c. Recovery can be backfilled for this adopted account (Commit B path), offline.
            Assert.That(account.HasRecoveryCode, Is.False, "a legacy account carries no recovery code yet");
            string code = setup.BackfillRecoveryCodeIfMissing();
            Assert.That(code, Is.Not.Null.And.Not.Empty, "a recovery code must be backfilled for the adopted owner");
            Assert.That(account.HasRecoveryCode, Is.True);

            AssertDataAllStillThere();
            AssertEveryModuleReachableForOwner();
        }

        [Test]
        public void PrePermissionDatabaseWithNoAccount_CreatesOwner_DataIntact_OwnerSignsIn_AllModulesReachable()
        {
            // 1. A very old install: real data, but NO account at all.
            BuildSchemaUpTo(PreUpdateVersion);
            SeedBusinessData();

            // 2. Upgrade + first-launch path. With data and no account, the owner MUST be created.
            RunMigrations();
            OwnerSetupService setup = BuildOwnerSetup(out ICustomerAccountService account);

            Assert.That(setup.DecideAction(), Is.EqualTo(OwnerSetupAction.CreateOwnerRequired),
                "data with no account must require creating the owner");
            Assert.That(setup.IsOwnerEstablished, Is.False);

            // A short password is refused and establishes nothing (install left exactly as it was).
            Assert.That(setup.CreateOwner("new.owner@sarl.dz", "SARL", "short").IsSuccess, Is.False);
            Assert.That(setup.IsOwnerEstablished, Is.False, "a failed creation must not mark setup complete");
            Assert.That(account.HasAccount, Is.False, "a failed creation must not create an account");

            // The real creation: returns the one-time recovery code, signs the owner in, marks complete.
            OptiPaie.Core.Primitives.Result<string> created = setup.CreateOwner("new.owner@sarl.dz", "SARL Ancienne", "nouveauMotDePasse1");
            Assert.That(created.IsSuccess, Is.True, created.Error);
            Assert.That(created.Value, Is.Not.Null.And.Not.Empty, "owner creation must issue a recovery code");
            Assert.That(setup.IsOwnerEstablished, Is.True);
            Assert.That(account.IsSignedIn, Is.True, "the new owner is signed in immediately");
            Assert.That(account.SignIn("new.owner@sarl.dz", "nouveauMotDePasse1"), Is.True);

            AssertDataAllStillThere();
            AssertEveryModuleReachableForOwner();
        }

        // ---- the upgrade a customer's app runs on first launch ----

        private void RunMigrations()
        {
            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            {
                int applied = new MigrationRunner(connection).Run();
                Assert.That(applied, Is.EqualTo(3), "exactly 0037..0039 must apply on a v36 database");
            }
            // After the real migration the new RowVersion columns exist: the schema is current.
            Assert.That(ColumnExists("Employees", "RowVersion"), Is.True);
            Assert.That(ColumnExists("Companies", "RowVersion"), Is.True);
        }

        private OwnerSetupService BuildOwnerSetup(out ICustomerAccountService account)
        {
            var factory = new SqliteConnectionFactory(_dbPath);
            var uow = new UnitOfWorkFactory(factory);
            var settings = new SettingsService(uow);
            account = new CustomerAccountService(settings, new PassthroughCipher(), NullLogger.Instance);
            // The real company service is the "has business data" probe — reads the migrated DB.
            var companies = new CompanyService(uow, new CompanyValidator(), AccessContext.Unrestricted);
            return new OwnerSetupService(settings, account, companies, NullLogger.Instance);
        }

        // ---- assertions: data survival + owner reach ----

        private void AssertDataAllStillThere()
        {
            Assert.That(Scalar("SELECT NameFr FROM Companies WHERE NameFr = 'SARL Ancienne';"),
                Is.EqualTo("SARL Ancienne"), "the pre-update company was lost");
            Assert.That(Count("SELECT COUNT(*) FROM Employees WHERE IsDeleted = 0;"), Is.EqualTo(3L),
                "the pre-update employees were lost");
            Assert.That(Count("SELECT COUNT(*) FROM EmploymentContracts;"), Is.EqualTo(3L),
                "the pre-update contracts were lost");
            Assert.That(Count("SELECT COUNT(*) FROM Payslips;"), Is.EqualTo(3L),
                "the pre-update payslips were lost");
            // Figures are byte-for-byte what the old engine wrote — never re-keyed or recomputed.
            Assert.That(Scalar("SELECT NetSalaire FROM Payslips WHERE EmployeeId = 1;"),
                Is.EqualTo("55000"), "a pre-update payslip figure was changed by the upgrade");
            Assert.That(Scalar("SELECT BaseCotisable FROM Payslips WHERE EmployeeId = 1;"),
                Is.EqualTo("60000"), "a pre-update payslip figure was changed by the upgrade");
        }

        /// <summary>
        /// Every module the owner should see is reachable. After adoption/creation the owner runs with
        /// no local user seated, so its effective access is Admin + IsOwner — it must be able to OPEN
        /// every screen, hold every write permission, and pass the owner-only gate.
        /// </summary>
        private static void AssertEveryModuleReachableForOwner()
        {
            IAccessContext owner = new EstablishedOwnerContext();

            foreach (string key in AllModuleKeys)
            {
                Assert.That(AccessPolicy.CanOpen(owner.CurrentRole, key), Is.True,
                    "the owner must be able to open '" + key + "' after upgrade");
            }

            foreach (Permission p in Enum.GetValues(typeof(Permission)).Cast<Permission>())
            {
                // RestoreDatabase is the one owner-ONLY operation: it is granted by no role and is
                // gated through RequireOwner (asserted just below), never through Require.
                if (p == Permission.RestoreDatabase)
                {
                    continue;
                }

                Assert.DoesNotThrow(() => AccessPolicy.Require(owner, p),
                    "the owner must hold every write permission (" + p + ")");
            }

            Assert.DoesNotThrow(() => AccessPolicy.RequireOwner(owner),
                "the established owner must pass the owner-only gate (database restore)");
        }

        // Every top-level navigation key the shell builds (core + registry modules + bottom + settings).
        private static readonly string[] AllModuleKeys =
        {
            "dashboard", "companies", "employees", "payroll", "cnas", "attestations",
            "attendance", "leave", "loans", "contracts", "performance", "assets", "training",
            "ats", "recruitment", "work_certificate", "archive", "reports", "settings"
        };

        private sealed class EstablishedOwnerContext : IAccessContext
        {
            public bool IsEnforced => true;                 // worst case: enforcement fully on
            public UserRole CurrentRole => UserRole.Admin;  // owner with no local user seated
            public bool IsOwner => true;
            public long? CurrentUserId => null;             // the owner has no local user id
        }

        // ---- fixture construction (same technique as UpgradePathTests) ----

        private void BuildSchemaUpTo(int maxVersion)
        {
            List<(int Version, string Name, string Sql)> scripts = LoadEmbeddedScripts()
                .Where(s => s.Version <= maxVersion)
                .OrderBy(s => s.Version)
                .ToList();

            Assert.That(scripts.Count, Is.GreaterThan(0), "no embedded migration scripts were found");

            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            {
                Exec(connection,
                    "CREATE TABLE IF NOT EXISTS SchemaMigrations (" +
                    "Version INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL, AppliedAtUtc TEXT NOT NULL);");

                foreach ((int version, string name, string sql) in scripts)
                {
                    Exec(connection, sql);
                    using (SQLiteCommand record = connection.CreateCommand())
                    {
                        record.CommandText =
                            "INSERT INTO SchemaMigrations (Version, Name, AppliedAtUtc) VALUES (@v, @n, @t);";
                        record.Parameters.AddWithValue("@v", version);
                        record.Parameters.AddWithValue("@n", name);
                        record.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                        record.ExecuteNonQuery();
                    }
                }
            }
        }

        private void SeedBusinessData()
        {
            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            {
                Exec(connection, "INSERT INTO Companies (NameFr, CreatedAtUtc) VALUES ('SARL Ancienne', '2024-01-01 00:00:00');");

                for (int i = 1; i <= 3; i++)
                {
                    using (SQLiteCommand emp = connection.CreateCommand())
                    {
                        emp.CommandText =
                            "INSERT INTO Employees (CompanyId, LastNameFr, FirstNameFr, HireDate, BaseSalary, CreatedAtUtc) " +
                            "VALUES (1, @ln, @fn, '2023-01-01', '60000', '2024-01-01 00:00:00');";
                        emp.Parameters.AddWithValue("@ln", "Nom" + i);
                        emp.Parameters.AddWithValue("@fn", "Prenom" + i);
                        emp.ExecuteNonQuery();
                    }

                    using (SQLiteCommand contract = connection.CreateCommand())
                    {
                        contract.CommandText =
                            "INSERT INTO EmploymentContracts (EmployeeId, Type, StartDate, BaseSalary, CreatedAtUtc) " +
                            "VALUES (@eid, 1, '2023-01-01', '60000', '2024-01-01 00:00:00');";
                        contract.Parameters.AddWithValue("@eid", i);
                        contract.ExecuteNonQuery();
                    }
                }

                Exec(connection,
                    "INSERT INTO PayrollRuns (CompanyId, PeriodYear, PeriodMonth, RunStatus, EngineVersion, CreatedAtUtc) " +
                    "VALUES (1, 2024, 6, 2, '1.0', '2024-06-30 00:00:00');");

                for (int i = 1; i <= 3; i++)
                {
                    using (SQLiteCommand slip = connection.CreateCommand())
                    {
                        slip.CommandText =
                            "INSERT INTO Payslips (RunId, EmployeeId, BaseCotisable, NetSalaire, GeneratedAtUtc, CreatedAtUtc) " +
                            "VALUES (1, @eid, '60000', '55000', '2024-06-30 00:00:00', '2024-06-30 00:00:00');";
                        slip.Parameters.AddWithValue("@eid", i);
                        slip.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>Creates a real (hashed) legacy account on the v36 DB, then signs it out — exactly
        /// the state an activated install is left in between runs.</summary>
        private void RegisterLegacyAccount(string email, string company, string password)
        {
            var factory = new SqliteConnectionFactory(_dbPath);
            var uow = new UnitOfWorkFactory(factory);
            var settings = new SettingsService(uow);
            var account = new CustomerAccountService(settings, new PassthroughCipher(), NullLogger.Instance);
            account.Register(email, company, password);
            account.SignOut();
        }

        // ---- embedded-script discovery (same convention the runner uses) ----

        private static IEnumerable<(int Version, string Name, string Sql)> LoadEmbeddedScripts()
        {
            const string marker = ".Sql.Migrations.";
            const string ext = ".sql";
            Assembly assembly = typeof(MigrationRunner).Assembly;

            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (resourceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0 ||
                    !resourceName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int markerIndex = resourceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                string afterMarker = resourceName.Substring(markerIndex + marker.Length);
                string scriptName = afterMarker.Substring(0, afterMarker.Length - ext.Length);
                int version = int.Parse(scriptName.Split('_')[0], NumberStyles.Integer, CultureInfo.InvariantCulture);

                string sql;
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (var reader = new StreamReader(stream))
                {
                    sql = reader.ReadToEnd();
                }

                yield return (version, scriptName, sql);
            }
        }

        // ---- introspection helpers ----

        private int MaxAppliedVersion()
        {
            return Convert.ToInt32(Scalar("SELECT MAX(Version) FROM SchemaMigrations;"), CultureInfo.InvariantCulture);
        }

        private bool ColumnExists(string table, string column)
        {
            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + table + ");";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(Convert.ToString(reader["name"]), column, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }

        private long Count(string sql) => Convert.ToInt64(Scalar(sql), CultureInfo.InvariantCulture);

        private object Scalar(string sql)
        {
            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
        }

        private static void Exec(SQLiteConnection connection, string sql)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private sealed class PassthroughCipher : ILocalCipher
        {
            public string Protect(string plaintext) => plaintext;
            public string Unprotect(string ciphertext) => ciphertext;
        }
    }
}
