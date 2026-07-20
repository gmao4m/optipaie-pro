using System;
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
                runner.Run();
            }

            return factory;
        }
    }
}
