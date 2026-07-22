namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle of a review cycle.</summary>
    public enum PerformanceCycleStatus
    {
        /// <summary>Created but not yet launched to reviewers.</summary>
        Draft = 1,

        /// <summary>Launched — reviews are being completed.</summary>
        Active = 2,

        /// <summary>Every assigned review is submitted.</summary>
        Completed = 3,

        /// <summary>Abandoned before completion.</summary>
        Cancelled = 4
    }
}
