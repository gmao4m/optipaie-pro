using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OptiPaie.Data.Migrations
{
    /// <summary>
    /// Applies pending, ordered, additive SQL migrations and records them in the
    /// <c>SchemaMigrations</c> table. Each migration runs in its own transaction so
    /// a failure leaves the database at its last consistent version. Already-applied
    /// migrations are skipped, so running this on every startup is safe and cheap.
    /// </summary>
    public sealed class MigrationRunner
    {
        private const string MigrationsResourceMarker = ".Sql.Migrations.";
        private const string SqlExtension = ".sql";

        private readonly SQLiteConnection _connection;

        /// <summary>Creates a runner that operates on an open connection.</summary>
        public MigrationRunner(SQLiteConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Applies all pending migrations in ascending version order.
        /// </summary>
        /// <returns>The number of migrations applied.</returns>
        public int Run()
        {
            EnsureMigrationsTable();

            HashSet<int> applied = GetAppliedVersions();
            List<MigrationScript> pending = LoadEmbeddedScripts()
                .Where(script => !applied.Contains(script.Version))
                .OrderBy(script => script.Version)
                .ToList();

            foreach (MigrationScript script in pending)
            {
                Apply(script);
            }

            return pending.Count;
        }

        private void EnsureMigrationsTable()
        {
            using (SQLiteCommand command = _connection.CreateCommand())
            {
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS SchemaMigrations (" +
                    "Version INTEGER NOT NULL PRIMARY KEY, " +
                    "Name TEXT NOT NULL, " +
                    "AppliedAtUtc TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }
        }

        private HashSet<int> GetAppliedVersions()
        {
            var versions = new HashSet<int>();

            using (SQLiteCommand command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT Version FROM SchemaMigrations;";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        versions.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
                    }
                }
            }

            return versions;
        }

        private void Apply(MigrationScript script)
        {
            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                try
                {
                    using (SQLiteCommand command = _connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = script.Sql;
                        command.ExecuteNonQuery();
                    }

                    using (SQLiteCommand record = _connection.CreateCommand())
                    {
                        record.Transaction = transaction;
                        record.CommandText =
                            "INSERT INTO SchemaMigrations (Version, Name, AppliedAtUtc) " +
                            "VALUES (@Version, @Name, @AppliedAtUtc);";
                        record.Parameters.AddWithValue("@Version", script.Version);
                        record.Parameters.AddWithValue("@Name", script.Name);
                        record.Parameters.AddWithValue(
                            "@AppliedAtUtc",
                            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                        record.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static IEnumerable<MigrationScript> LoadEmbeddedScripts()
        {
            Assembly assembly = typeof(MigrationRunner).Assembly;

            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (resourceName.IndexOf(MigrationsResourceMarker, StringComparison.OrdinalIgnoreCase) < 0 ||
                    !resourceName.EndsWith(SqlExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string scriptName = ExtractScriptName(resourceName);
                int version = ParseVersion(scriptName, resourceName);
                string sql = ReadResource(assembly, resourceName);

                yield return new MigrationScript(version, scriptName, sql);
            }
        }

        private static string ExtractScriptName(string resourceName)
        {
            int markerIndex = resourceName.IndexOf(MigrationsResourceMarker, StringComparison.OrdinalIgnoreCase);
            string afterMarker = resourceName.Substring(markerIndex + MigrationsResourceMarker.Length);
            return afterMarker.Substring(0, afterMarker.Length - SqlExtension.Length);
        }

        private static int ParseVersion(string scriptName, string resourceName)
        {
            string prefix = scriptName.Split('_')[0];

            if (!int.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
            {
                throw new InvalidOperationException(
                    "Migration script '" + resourceName + "' must start with a numeric version (e.g. 0001_Name.sql).");
            }

            return version;
        }

        private static string ReadResource(Assembly assembly, string resourceName)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded migration not found: " + resourceName);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
