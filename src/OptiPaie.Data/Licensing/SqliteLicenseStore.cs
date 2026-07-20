using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using Dapper;
using OptiPaie.Core.Licensing;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Licensing
{
    /// <summary>
    /// Persists the local license cache in the shared SQLite database (tables
    /// <c>LicenseState</c> and <c>LicenseModules</c>, added by migration 0006). It
    /// opens its own connection per call and is completely independent of the payroll
    /// business repositories / unit of work — licensing is a separate concern.
    /// </summary>
    public sealed class SqliteLicenseStore : ILicenseStore
    {
        private const string SelectColumns =
            "ProductKey, LicenseKey, CompanyName, Email, DeviceId, Status, SignedToken, " +
            "ActivatedAtUtc, LastValidationUtc, ExpiresAtUtc, GraceUntilUtc, LastSeenUtc";

        private readonly SqliteConnectionFactory _connectionFactory;

        public SqliteLicenseStore(SqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public StoredLicense Load()
        {
            using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
            {
                return connection.QuerySingleOrDefault<StoredLicense>(
                    "SELECT " + SelectColumns + " FROM LicenseState WHERE Id = 1;");
            }
        }

        public IReadOnlyList<string> LoadModules()
        {
            using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
            {
                return connection.Query<string>(
                    "SELECT ModuleKey FROM LicenseModules WHERE Enabled = 1 ORDER BY ModuleKey;").ToList();
            }
        }

        public void Save(StoredLicense license, IEnumerable<string> enabledModuleKeys)
        {
            if (license == null)
            {
                throw new ArgumentNullException(nameof(license));
            }

            List<string> modules = (enabledModuleKeys ?? Enumerable.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    connection.Execute(
                        "INSERT OR REPLACE INTO LicenseState " +
                        "(Id, ProductKey, LicenseKey, CompanyName, Email, DeviceId, Status, SignedToken, " +
                        " ActivatedAtUtc, LastValidationUtc, ExpiresAtUtc, GraceUntilUtc, LastSeenUtc, UpdatedAtUtc) " +
                        "VALUES (1, @ProductKey, @LicenseKey, @CompanyName, @Email, @DeviceId, @Status, @SignedToken, " +
                        " @ActivatedAtUtc, @LastValidationUtc, @ExpiresAtUtc, @GraceUntilUtc, @LastSeenUtc, @UpdatedAtUtc);",
                        new
                        {
                            license.ProductKey,
                            license.LicenseKey,
                            license.CompanyName,
                            license.Email,
                            license.DeviceId,
                            license.Status,
                            license.SignedToken,
                            license.ActivatedAtUtc,
                            license.LastValidationUtc,
                            license.ExpiresAtUtc,
                            license.GraceUntilUtc,
                            license.LastSeenUtc,
                            UpdatedAtUtc = nowIso
                        },
                        transaction);

                    connection.Execute("DELETE FROM LicenseModules;", null, transaction);

                    foreach (string key in modules)
                    {
                        connection.Execute(
                            "INSERT INTO LicenseModules (ModuleKey, Enabled) VALUES (@key, 1);",
                            new { key }, transaction);
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

        public void UpdateLastSeen(string lastSeenUtcIso)
        {
            if (string.IsNullOrWhiteSpace(lastSeenUtcIso))
            {
                return;
            }

            using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
            {
                connection.Execute(
                    "UPDATE LicenseState SET LastSeenUtc = @lastSeenUtcIso WHERE Id = 1;",
                    new { lastSeenUtcIso });
            }
        }

        public void Clear()
        {
            using (SQLiteConnection connection = _connectionFactory.CreateOpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    connection.Execute("DELETE FROM LicenseState;", null, transaction);
                    connection.Execute("DELETE FROM LicenseModules;", null, transaction);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }
    }
}
