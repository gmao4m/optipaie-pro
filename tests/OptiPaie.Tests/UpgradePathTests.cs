using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Upgrade-path guarantee. A REAL customer database created on the PREVIOUS shipped schema
    /// (frozen at migration 0032, before Recruitment 0033 and AuditLog.CompanyId 0034) and
    /// populated with live data must migrate to the current schema with NO error and NO data loss.
    /// <para>
    /// This is the exact risk carried by the non-idempotent <c>ALTER TABLE ... ADD COLUMN</c>
    /// statements in 0033/0034: they must apply cleanly on a POPULATED production database, exactly
    /// once, and leave every pre-existing row intact. A headless service test that starts from a
    /// fresh, fully-migrated database never exercises this path — this test starts from a genuine
    /// older database and drives the real <see cref="MigrationRunner"/> forward, the same call the
    /// customer's app makes on startup.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class UpgradePathTests
    {
        // The last schema version shipped before this update. A customer on the previous release
        // has a database recorded at exactly this version; the update must carry it to HEAD.
        private const int PreUpdateVersion = 32;

        private string _directory;
        private string _dbPath;

        [SetUp]
        public void SetUp()
        {
            SqliteTypeHandlers.Register();
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _dbPath = Path.Combine(_directory, "customer.db");
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        [Test]
        public void RealPreUpdateDatabase_MigratesToCurrentSchema_WithNoLoss()
        {
            // 1. Reproduce a real customer database frozen at the previous shipped schema (v32),
            //    built from the very same embedded migration scripts the product ships.
            BuildSchemaUpTo(PreUpdateVersion);
            SeedPreUpdateData();

            Assert.That(AppliedVersions().Max(), Is.EqualTo(PreUpdateVersion),
                "the pre-update fixture is not frozen at v32");
            Assert.That(ColumnExists("AuditLog", "CompanyId"), Is.False,
                "the fixture already carries a post-update column — it is not a genuine pre-update DB");

            // 2. Run the REAL migration runner, exactly as a customer's app does on startup.
            int applied;
            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            {
                applied = new MigrationRunner(connection).Run();
            }

            // 3. Exactly the pending migrations (0033, 0034, 0035) applied — cleanly, no throw.
            Assert.That(applied, Is.EqualTo(3), "expected exactly 0033 + 0034 + 0035 to apply on a v32 database");
            Assert.That(AppliedVersions(), Does.Contain(33).And.Contain(34).And.Contain(35));

            // 4. The new schema is present.
            Assert.That(ColumnExists("JobPostings", "ContractType"), Is.True, "0033 did not add JobPostings.ContractType");
            Assert.That(ColumnExists("Candidates", "ClosureType"), Is.True, "0033 did not add Candidates.ClosureType");
            Assert.That(ColumnExists("AuditLog", "CompanyId"), Is.True, "0034 did not add AuditLog.CompanyId");
            Assert.That(TableExists("Interviews"), Is.True, "0033 did not create the Interviews table");
            // 0035 seeds ONE global « Congé de récupération » row into the existing LeaveTypes catalogue,
            // additively, without disturbing the populated pre-update database.
            Assert.That(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM LeaveTypes WHERE Code = 'RECOVERY' AND CompanyId IS NULL;"), CultureInfo.InvariantCulture),
                Is.EqualTo(1L), "0035 did not seed the global Congé de récupération type exactly once");

            // 5. No data loss: every pre-update row survives untouched, and the new nullable column
            //    is NULL for rows written before the update (never back-filled with a guessed value).
            Assert.That(Scalar("SELECT NameFr FROM Companies WHERE NameFr = 'SARL Ancienne';"),
                Is.EqualTo("SARL Ancienne"), "the pre-update company row was lost by the migration");
            Assert.That(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM AuditLog WHERE Summary = 'ancienne entrée';"), CultureInfo.InvariantCulture),
                Is.EqualTo(1L), "the pre-update audit row was lost by the migration");
            Assert.That(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM AuditLog WHERE Summary = 'ancienne entrée' AND CompanyId IS NULL;"), CultureInfo.InvariantCulture),
                Is.EqualTo(1L), "a pre-update audit row must keep CompanyId = NULL, not a guessed value");

            // 6. Idempotent: re-running on the now-current DB applies nothing and never throws
            //    (the ADD COLUMN statements must not fire a second time).
            int second;
            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            {
                second = new MigrationRunner(connection).Run();
            }
            Assert.That(second, Is.EqualTo(0), "a fully-migrated database must apply no migrations on re-run");
        }

        // ---- fixture construction: build the DB from the shipped scripts, but stop at maxVersion ----

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

        private void SeedPreUpdateData()
        {
            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            {
                using (SQLiteCommand company = connection.CreateCommand())
                {
                    company.CommandText =
                        "INSERT INTO Companies (NameFr, CreatedAtUtc) VALUES ('SARL Ancienne', @t);";
                    company.Parameters.AddWithValue("@t", "2024-01-01 00:00:00");
                    company.ExecuteNonQuery();
                }

                // An audit row written on the OLD schema (no CompanyId column exists yet).
                using (SQLiteCommand audit = connection.CreateCommand())
                {
                    audit.CommandText =
                        "INSERT INTO AuditLog (EntityType, EntityId, Action, Summary, CreatedAtUtc) " +
                        "VALUES ('Employee', 7, 1, 'ancienne entrée', @t);";
                    audit.Parameters.AddWithValue("@t", "2024-06-15 09:30:00");
                    audit.ExecuteNonQuery();
                }
            }
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

        private HashSet<int> AppliedVersions()
        {
            var versions = new HashSet<int>();
            using (SQLiteConnection connection = new SqliteConnectionFactory(_dbPath).CreateOpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Version FROM SchemaMigrations;";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        versions.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
                }
            }
            return versions;
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

        private bool TableExists(string table)
        {
            object result = Scalar("SELECT name FROM sqlite_master WHERE type = 'table' AND name = '" + table + "';");
            return result != null && result != DBNull.Value;
        }

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
    }
}
