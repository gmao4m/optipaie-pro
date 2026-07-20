using System;
using System.IO;
using OptiPaie.Common.Constants;

namespace OptiPaie.Common.Configuration
{
    /// <summary>
    /// Immutable machine-level bootstrap configuration, read before the database is
    /// opened (the data directory and the startup language). Everything else lives
    /// in the database. The actual loading from the app config file is performed by
    /// the infrastructure layer; this object only holds and derives the values.
    /// </summary>
    public sealed class AppConfiguration
    {
        /// <summary>Directory holding the database and backups.</summary>
        public string DataDirectory { get; }

        /// <summary>Startup language code.</summary>
        public string DefaultLanguage { get; }

        /// <summary>Full path to the SQLite database file.</summary>
        public string DatabaseFilePath => Path.Combine(DataDirectory, AppConstants.DatabaseFileName);

        /// <summary>Full path to the backups directory.</summary>
        public string BackupDirectory => Path.Combine(DataDirectory, AppConstants.BackupFolderName);

        /// <summary>Creates a bootstrap configuration.</summary>
        public AppConfiguration(string dataDirectory, string defaultLanguage)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentException("Data directory must be provided.", nameof(dataDirectory));
            }

            DataDirectory = dataDirectory;
            DefaultLanguage = string.IsNullOrWhiteSpace(defaultLanguage)
                ? AppConstants.DefaultLanguage
                : defaultLanguage;
        }
    }
}
