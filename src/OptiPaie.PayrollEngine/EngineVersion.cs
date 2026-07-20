namespace OptiPaie.PayrollEngine
{
    /// <summary>
    /// Identifies the engine binary and the calculation algorithm. Stored with every
    /// payslip so an archived calculation can be reproduced exactly even after the
    /// engine or the law evolves. (The legal version comes from the active profile.)
    /// </summary>
    public static class EngineVersion
    {
        /// <summary>Version of the engine implementation.</summary>
        public const string Version = "1.0.0";

        /// <summary>Version of the calculation algorithm/pipeline.</summary>
        public const string CalculationVersion = "1.0";
    }
}
