using System;
using System.Data.SQLite;

namespace OptiPaie.Data.Context
{
    /// <summary>
    /// Creates fully-configured, open SQLite connections.
    /// <para>
    /// Connection settings are tuned for a single-user desktop app on low-spec /
    /// Windows 7 hardware: WAL journaling and NORMAL synchronisation for fast writes
    /// on slow HDDs, foreign keys enforced, ISO-8601 UTC date storage, a generous
    /// busy timeout and an in-memory temp store. The same factory is reused by the
    /// migration runner and (in the next step) the repositories.
    /// </para>
    /// </summary>
    public sealed class SqliteConnectionFactory
    {
        private readonly string _connectionString;

        /// <summary>The connection string built for the database file.</summary>
        public string ConnectionString => _connectionString;

        /// <summary>Builds a factory for the SQLite database at the given path.</summary>
        public SqliteConnectionFactory(string databaseFilePath)
        {
            if (string.IsNullOrWhiteSpace(databaseFilePath))
            {
                throw new ArgumentException("Database file path must be provided.", nameof(databaseFilePath));
            }

            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = databaseFilePath,
                Version = 3,
                ForeignKeys = true,
                JournalMode = SQLiteJournalModeEnum.Wal,
                SyncMode = SynchronizationModes.Normal,
                DateTimeFormat = SQLiteDateFormats.ISO8601,
                DateTimeKind = DateTimeKind.Utc,
                DefaultTimeout = 30,
                BusyTimeout = 30000,
                CacheSize = 2000,
                Pooling = true,
                FailIfMissing = false
            };

            _connectionString = builder.ToString();
        }

        /// <summary>
        /// Creates and opens a connection, applying the per-connection PRAGMAs that
        /// cannot be set through the connection string.
        /// </summary>
        public SQLiteConnection CreateOpenConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            using (SQLiteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA temp_store = MEMORY;";
                pragma.ExecuteNonQuery();
            }

            return connection;
        }
    }
}
