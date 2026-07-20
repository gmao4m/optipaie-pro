namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Immutable audit-trace entry for one stage of the calculation, powering the
    /// "explain this calculation" view and the engine validation tests.
    /// </summary>
    public sealed class PayrollCalculationStep
    {
        /// <summary>Stable key of the stage (e.g. "GROSS", "CNAS", "IRG_BRUT", "ABATTEMENT", "IRG", "NET").</summary>
        public string Key { get; }

        /// <summary>The amount the stage produced.</summary>
        public decimal Amount { get; }

        /// <summary>Human-readable explanation of how the amount was obtained.</summary>
        public string Detail { get; }

        /// <summary>Creates an immutable trace step.</summary>
        public PayrollCalculationStep(string key, decimal amount, string detail)
        {
            Key = key;
            Amount = amount;
            Detail = detail;
        }
    }
}
