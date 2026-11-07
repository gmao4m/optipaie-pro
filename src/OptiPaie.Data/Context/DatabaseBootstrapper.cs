using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using OptiPaie.Common.Configuration;
using OptiPaie.Data.Migrations;

namespace OptiPaie.Data.Context
{
    /// <summary>
    /// Initialises the database layer at application startup: ensures the data and
    /// backup folders exist, registers the canonical decimal type handling, creates
    /// the database file if needed and applies any pending migrations. Returns the
    /// connection factory the rest of the data layer will use.
    /// </summary>
    public sealed class DatabaseBootstrapper
    {
        private readonly AppConfiguration _configuration;

        /// <summary>Creates a bootstrapper for the given application configuration.</summary>
        public DatabaseBootstrapper(AppConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Performs initialisation and returns a ready-to-use connection factory.
        /// </summary>
        public SqliteConnectionFactory Initialize()
        {
            Directory.CreateDirectory(_configuration.DataDirectory);
            Directory.CreateDirectory(_configuration.BackupDirectory);

            SqliteTypeHandlers.Register();

            var factory = new SqliteConnectionFactory(_configuration.DatabaseFilePath);

            using (var connection = factory.CreateOpenConnection())
            {
                var runner = new MigrationRunner(connection);

                // D7 (safety): before changing the schema of an EXISTING database, take a
                // consistent copy so a failed or lossy migration can always be rolled back by
                // the user. We only back up when (a) there are pending migrations AND (b) the
                // database already carries applied migrations (i.e. it holds real data — a
                // brand-new file created this same run has nothing to protect). The WAL is
                // checkpointed and the SQLite Online Backup API is used (never a raw file copy).
                IReadOnlyList<int> pending = runner.GetPendingVersions();
                int currentVersion = GetAppliedMaxVersion(connection);
                if (pending.Count > 0 && currentVersion > 0)
                {
                    BackupBeforeMigration(connection, currentVersion);
                }

                runner.Run();
            }

            return factory;
        }

        /// <summary>Highest applied migration version, or 0 for a fresh/empty database.</summary>
        private static int GetAppliedMaxVersion(SQLiteConnection connection)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT IFNULL(MAX(Version), 0) FROM SchemaMigrations;";
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Writes a consistent copy of the live database to the backup folder before any pending
        /// migration runs, using a WAL checkpoint + the SQLite Online Backup API. If the copy
        /// cannot be made we do NOT silently migrate without a safety net — the exception is
        /// surfaced with a clear message, because a schema change without a backup is exactly the
        /// data-loss risk this guard removes.
        /// </summary>
        private void BackupBeforeMigration(SQLiteConnection source, int currentVersion)
        {
            string fileName = "pre-migration-v" + currentVersion.ToString(CultureInfo.InvariantCulture)
                + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".db";
            string destinationPath = Path.Combine(_configuration.BackupDirectory, fileName);

            try
            {
                using (SQLiteCommand checkpoint = source.CreateCommand())
                {
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpoint.ExecuteNonQuery();
                }

                using (var destination = new SQLiteConnection("Data Source=" + destinationPath + ";Version=3;"))
                {
                    destination.Open();
                    source.BackupDatabase(destination, "main", "main", -1, null, 0);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Impossible de créer une sauvegarde de sécurité avant la mise à jour de la base de données (« "
                    + destinationPath + " »). La mise à jour est interrompue pour protéger vos données. Détail : "
                    + ex.Message, ex);
            }
        }
    }
}
