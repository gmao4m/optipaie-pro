namespace OptiPaie.Common.Constants
{
    /// <summary>
    /// Stable keys for the application/UI preferences stored in the Settings table.
    /// Using constants prevents typo-driven bugs across layers.
    /// </summary>
    public static class SettingKeys
    {
        /// <summary>Current UI language code ("fr" / "ar").</summary>
        public const string Language = "LANGUAGE";

        /// <summary>DevExpress skin/theme name.</summary>
        public const string Theme = "THEME";

        /// <summary>Backup directory path.</summary>
        public const string BackupDirectory = "BACKUP_DIRECTORY";

        /// <summary>Default company pre-selected in the UI.</summary>
        public const string DefaultCompanyId = "DEFAULT_COMPANY_ID";

        /// <summary>Rounding scale used for payroll amounts (0 = whole dinar, 2 = centime).</summary>
        public const string RoundingScale = "ROUNDING_SCALE";

        /// <summary>Default overtime majoration as a fraction (e.g. 0.5 for 50%).</summary>
        public const string OvertimeMajoration = "OVERTIME_MAJORATION";
    }
}
