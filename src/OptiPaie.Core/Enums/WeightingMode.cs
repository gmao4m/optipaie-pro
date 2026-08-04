namespace OptiPaie.Core.Enums
{
    /// <summary>How an evaluation template combines its criteria into the overall score.</summary>
    public enum WeightingMode
    {
        /// <summary>All criteria count equally — the total is their plain average.</summary>
        Simple = 1,

        /// <summary>Each criterion carries a weight percentage (summing to 100).</summary>
        Weighted = 2
    }
}
