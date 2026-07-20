namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>
    /// The IRG abattement parameters: a rate applied to the gross IRG, clamped
    /// between a monthly minimum and maximum.
    /// </summary>
    public sealed class AbattementRule
    {
        /// <summary>Abattement rate as a fraction (e.g. 0.40 for 40%).</summary>
        public decimal Rate { get; }

        /// <summary>Monthly minimum abattement (dinars).</summary>
        public decimal Min { get; }

        /// <summary>Monthly maximum abattement (dinars).</summary>
        public decimal Max { get; }

        public AbattementRule(decimal rate, decimal min, decimal max)
        {
            Rate = rate;
            Min = min;
            Max = max;
        }
    }
}
