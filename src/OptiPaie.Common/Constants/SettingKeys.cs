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

        /// <summary>
        /// "1" = the legal deductions (IRG + CNAS) are computed on every NEW payslip (the default),
        /// "0" = they are skipped. Read FAIL-SAFE: a missing row, NULL or any value other than an
        /// explicit OFF means ON (see LegalDeductionsSetting.IsEnabled).
        /// </summary>
        public const string LegalDeductionsEnabled = "LEGAL_DEDUCTIONS_ENABLED";
    }

    /// <summary>
    /// The single fail-safe reader for <see cref="SettingKeys.LegalDeductionsEnabled"/>. Deductions are
    /// disabled ONLY by an explicit, well-formed OFF ("0" or "false", case- and space-insensitive).
    /// Everything else — no row, NULL, empty, garbage — reads as ON, so a read failure can never
    /// silently stop the legal deductions.
    /// </summary>
    public static class LegalDeductionsSetting
    {
        public const string On = "1";
        public const string Off = "0";

        public static bool IsEnabled(string rawValue)
        {
            if (rawValue == null) return true;
            string v = rawValue.Trim();
            bool explicitOff = v == Off || string.Equals(v, "false", System.StringComparison.OrdinalIgnoreCase);
            return !explicitOff;
        }
    }
}
