using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using OptiPaie.Common.Configuration;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;

namespace OptiPaie.Tests
{
    /// <summary>
    /// E2E audit — Section 5 (data integrity) + the D7 pre-migration safety backup.
    /// Evidence, not faith: every test runs the REAL <see cref="DatabaseBootstrapper"/> /
    /// <see cref="MigrationRunner"/> and inspects the real file system and PRAGMA output.
    /// </summary>
    [TestFixture]
    public sealed class AuditDataIntegrityTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-audit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private AppConfiguration Config() => new AppConfiguration(_dir, "fr");

        private static string Scalar(SQLiteConnection c, string sql)
        {
            using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
        }

        private static int RowCount(SQLiteConnection c, string sql)
        {
            using (SQLiteCommand cmd = c.CreateCommand())
            using (SQLiteDataReader r = cmd.ExecuteReaderFor(sql))
            {
                int n = 0; while (r.Read()) n++; return n;
            }
        }

        private static string[] PreMigrationBackups(AppConfiguration cfg) =>
            Directory.Exists(cfg.BackupDirectory)
                ? Directory.GetFiles(cfg.BackupDirectory, "pre-migration-*.db")
                : new string[0];

        // D4 — a freshly migrated database is structurally sound; a first install takes no backup.
        [Test]
        public void FreshDatabase_PassesIntegrityAndForeignKeyCheck_AndTakesNoNeedlessBackup()
        {
            AppConfiguration cfg = Config();
            SqliteConnectionFactory factory = new DatabaseBootstrapper(cfg).Initialize();

            using (SQLiteConnection c = factory.CreateOpenConnection())
            {
                Assert.That(Scalar(c, "PRAGMA integrity_check;"), Is.EqualTo("ok"), "PRAGMA integrity_check");
                Assert.That(RowCount(c, "PRAGMA foreign_key_check;"), Is.EqualTo(0), "PRAGMA foreign_key_check must return no rows");
            }

            Assert.That(PreMigrationBackups(cfg), Is.Empty,
                "a fresh install starts at version 0 — nothing to protect, so no pre-migration backup");
        }

        // The trigger input for the backup decision.
        [Test]
        public void GetPendingVersions_FreshReturnsAll_ThenEmptyAfterRun()
        {
            var factory = new SqliteConnectionFactory(Config().DatabaseFilePath);
            using (SQLiteConnection c = factory.CreateOpenConnection())
            {
                var runner = new MigrationRunner(c);
                Assert.That(runner.GetPendingVersions().Count, Is.GreaterThan(0), "a fresh DB has pending migrations");
                runner.Run();
                Assert.That(runner.GetPendingVersions(), Is.Empty, "after Run nothing is pending");
            }
        }

        // D7 — upgrading an EXISTING database writes a consistent safety copy BEFORE the schema is touched.
        [Test]
        public void UpgradingExistingDatabase_WritesIntactPreMigrationBackup_BeforeApplying()
        {
            AppConfiguration cfg = Config();

            // 1) A real, fully-migrated customer database.
            SqliteConnectionFactory factory = new DatabaseBootstrapper(cfg).Initialize();
            Assert.That(PreMigrationBackups(cfg), Is.Empty, "baseline: the fresh install wrote no backup");

            // 2) Rewind the bookkeeping by one version so the newest migration looks pending again.
            //    The schema is already at that version, so the re-apply will fail — which is exactly
            //    why we assert the SAFETY COPY is taken BEFORE the schema is touched.
            using (SQLiteConnection c = factory.CreateOpenConnection())
            {
                int top = Convert.ToInt32(Scalar(c, "SELECT MAX(Version) FROM SchemaMigrations;"), CultureInfo.InvariantCulture);
                using (SQLiteCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM SchemaMigrations WHERE Version = @v;";
                    cmd.Parameters.AddWithValue("@v", top);
                    cmd.ExecuteNonQuery();
                }
            }
            SQLiteConnection.ClearAllPools();

            // 3) Re-run the bootstrapper: version (top-1) applied + {top} pending → it must back up first.
            try { new DatabaseBootstrapper(cfg).Initialize(); }
            catch (Exception) { /* expected: re-applying an already-present schema change throws */ }

            string[] backups = PreMigrationBackups(cfg);
            Assert.That(backups.Length, Is.EqualTo(1), "exactly one pre-migration safety backup must exist before the upgrade");

            using (var c = new SQLiteConnection("Data Source=" + backups[0] + ";Version=3;"))
            {
                c.Open();
                Assert.That(Scalar(c, "PRAGMA integrity_check;"), Is.EqualTo("ok"), "the safety backup must itself be an intact database");
            }
        }
    }

    internal static class SqliteCommandExtensions
    {
        public static SQLiteDataReader ExecuteReaderFor(this SQLiteCommand cmd, string sql)
        {
            cmd.CommandText = sql;
            return cmd.ExecuteReader();
        }
    }
}
