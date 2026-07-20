using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using OptiPaie.Common.Configuration;
using OptiPaie.Core.Interfaces;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Backup
{
    /// <summary>
    /// SQLite implementation of <see cref="IDatabaseBackupProvider"/>.
    /// Backups use the SQLite Online Backup API after checkpointing the WAL, which
    /// produces a consistent copy of a live database (never a raw file copy).
    /// Restore clears connection pools and removes stale WAL/SHM side files before
    /// replacing the database.
    /// </summary>
    public sealed class SqliteBackupProvider : IDatabaseBackupProvider
    {
        private readonly SqliteConnectionFactory _connectionFactory;
        private readonly AppConfiguration _configuration;

        public SqliteBackupProvider(SqliteConnectionFactory connectionFactory, AppConfiguration configuration)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public void Backup(string destinationFilePath)
        {
            using (SQLiteConnection source = _connectionFactory.CreateOpenConnection())
            {
                using (SQLiteCommand checkpoint = source.CreateCommand())
                {
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpoint.ExecuteNonQuery();
                }

                using (var destination = new SQLiteConnection("Data Source=" + destinationFilePath + ";Version=3;"))
                {
                    destination.Open();
                    source.BackupDatabase(destination, "main", "main", -1, null, 0);
                }
            }
        }

        public bool VerifyDatabaseFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                using (var connection = new SQLiteConnection("Data Source=" + filePath + ";Version=3;FailIfMissing=True;"))
                {
                    connection.Open();
                    using (SQLiteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA integrity_check;";
                        var result = command.ExecuteScalar() as string;
                        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public int GetSchemaVersion()
        {
            try
            {
                using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT IFNULL(MAX(Version), 0) FROM SchemaMigrations;";
                    object value = command.ExecuteScalar();
                    return value == null || value == DBNull.Value
                        ? 0
                        : Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                return 0;
            }
        }

        public void RestoreFrom(string backupFilePath)
        {
            SQLiteConnection.ClearAllPools();

            string databasePath = _configuration.DatabaseFilePath;
            DeleteIfExists(databasePath + "-wal");
            DeleteIfExists(databasePath + "-shm");

            File.Copy(backupFilePath, databasePath, true);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
