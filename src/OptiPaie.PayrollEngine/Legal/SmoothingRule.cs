namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>
    /// The low-income smoothing (transition) rule applied to taxable bases between
    /// <see cref="LowerBound"/> and <see cref="UpperBound"/>:
    /// IRG = afterAbattement × (MultiplierNumerator / MultiplierDenominator)
    ///       − (SubtrahendNumerator / SubtrahendDenominator).
    /// Constants are kept as exact numerator/denominator pairs to preserve precision.
    /// </summary>
    public sealed class SmoothingRule
    {
        public decimal LowerBound { get; }
        public decimal UpperBound { get; }
        public decimal MultiplierNumerator { get; }
        public decimal MultiplierDenominator { get; }
        public decimal SubtrahendNumerator { get; }
        public decimal SubtrahendDenominator { get; }

        public SmoothingRule(
            decimal lowerBound,
            decimal upperBound,
            decimal multiplierNumerator,
            decimal multiplierDenominator,
            decimal subtrahendNumerator,
            decimal subtrahendDenominator)
        {
            LowerBound = lowerBound;
            UpperBound = upperBound;
            MultiplierNumerator = multiplierNumerator;
            MultiplierDenominator = multiplierDenominator;
            SubtrahendNumerator = subtrahendNumerator;
            SubtrahendDenominator = subtrahendDenominator;
        }
    }
}
