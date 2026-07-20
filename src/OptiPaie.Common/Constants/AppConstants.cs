namespace OptiPaie.Common.Constants
{
    /// <summary>
    /// Application-wide constant values. Centralised so names, file names and
    /// defaults are defined exactly once (DRY).
    /// </summary>
    public static class AppConstants
    {
        /// <summary>Commercial product name.</summary>
        public const string ApplicationName = "OptiPaie DZ";

        /// <summary>Vendor name.</summary>
        public const string CompanyName = "OptiPaie";

        /// <summary>Folder (under %AppData%) holding the database and backups.</summary>
        public const string DataFolderName = "OptiPaie DZ";

        /// <summary>SQLite database file name.</summary>
        public const string DatabaseFileName = "optipaie.db";

        /// <summary>Sub-folder name used for database backups.</summary>
        public const string BackupFolderName = "Backups";

        /// <summary>Log file name.</summary>
        public const string LogFileName = "optipaie.log";

        /// <summary>French language code.</summary>
        public const string LanguageFrench = "fr";

        /// <summary>Arabic language code.</summary>
        public const string LanguageArabic = "ar";

        /// <summary>Default UI language when none is configured.</summary>
        public const string DefaultLanguage = LanguageFrench;
    }
}
