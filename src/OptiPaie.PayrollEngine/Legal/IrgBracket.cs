namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>
    /// One marginal bracket of the monthly IRG barème: a rate applied to the portion
    /// of the taxable base that falls between <see cref="LowerBound"/> (exclusive)
    /// and <see cref="UpperBound"/> (inclusive, or open-ended when null).
    /// </summary>
    public sealed class IrgBracket
    {
        /// <summary>Lower bound (the amount above which this bracket's rate applies).</summary>
        public decimal LowerBound { get; }

        /// <summary>Upper bound, or null for the top open-ended bracket.</summary>
        public decimal? UpperBound { get; }

        /// <summary>Marginal rate as a fraction (e.g. 0.23 for 23%).</summary>
        public decimal Rate { get; }

        public IrgBracket(decimal lowerBound, decimal? upperBound, decimal rate)
        {
            LowerBound = lowerBound;
            UpperBound = upperBound;
            Rate = rate;
        }
    }
}
