namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle of a performance review.</summary>
    public enum PerformanceStatus
    {
        /// <summary>Being filled in — criteria and scores can still change.</summary>
        Draft = 1,

        /// <summary>Finalised — the overall score is locked.</summary>
        Completed = 2
    }
}
