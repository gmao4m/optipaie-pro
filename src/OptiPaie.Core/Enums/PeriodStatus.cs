namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle of an evaluation period.</summary>
    public enum PeriodStatus
    {
        /// <summary>Evaluations can still be created and edited.</summary>
        Open = 1,

        /// <summary>Locked — the period is finished and its evaluations are frozen.</summary>
        Closed = 2
    }
}
