using System;
using System.Data.SQLite;
using System.Globalization;
using Dapper;
using OptiPaie.Core.Licensing;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Licensing
{
    /// <summary>
    /// Persists the encrypted trial blob in the shared SQLite database (table
    /// <c>TrialState</c>, migration 0007). Opens its own connection per call and is
    /// independent of the business repositories.
    /// </summary>
    public sealed class SqliteTrialStore : ITrialStore
    {
        private readonly SqliteConnectionFactory _connectionFactory;

        public SqliteTrialStore(SqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public string Load()
        {
            using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
            {
                return connection.QuerySingleOrDefault<string>(
                    "SELECT Blob FROM TrialState WHERE Id = 1;");
            }
        }

        public void Save(string blob)
        {
            string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
            {
                connection.Execute(
                    "INSERT OR REPLACE INTO TrialState (Id, Blob, UpdatedAtUtc) VALUES (1, @blob, @nowIso);",
                    new { blob, nowIso });
            }
        }

        public void Clear()
        {
            using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
            {
                connection.Execute("DELETE FROM TrialState;");
            }
        }
    }
}
