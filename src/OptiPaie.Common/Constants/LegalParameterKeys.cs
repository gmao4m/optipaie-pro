namespace OptiPaie.Common.Constants
{
    /// <summary>
    /// Stable keys for the configurable payroll legal parameters stored in the
    /// Configuration table. The IRG barème, abattement and lissage are NOT here —
    /// per the approved decisions they are fixed in the engine.
    /// </summary>
    public static class LegalParameterKeys
    {
        /// <summary>CNAS employee contribution rate (fraction, e.g. 0.09).</summary>
        public const string CnasEmployeeRate = "CNAS_EMPLOYEE_RATE";

        /// <summary>CNAS employer contribution rate (fraction, e.g. 0.26).</summary>
        public const string CnasEmployerRate = "CNAS_EMPLOYER_RATE";

        /// <summary>Salaire National Minimum Garanti (dinars).</summary>
        public const string Snmg = "SNMG";
    }
}
