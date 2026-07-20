namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>The lissage (smoothing/étalement) method for non-monthly amounts.</summary>
    public enum LissageMethod
    {
        /// <summary>
        /// Differential method (specification §6): IRG on the spread amount =
        /// Σ over the concerned months of IRG(monthBase + share) − IRG(monthBase).
        /// </summary>
        Differential = 1
    }
}
