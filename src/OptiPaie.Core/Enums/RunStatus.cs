namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// Lifecycle state of a payroll run (a company + period batch).
    /// </summary>
    public enum RunStatus
    {
        /// <summary>Created and editable; can be recomputed freely.</summary>
        Draft = 1,

        /// <summary>Calculated and finalised, not yet archived.</summary>
        Generated = 2,

        /// <summary>Archived and immutable; reprintable but never recalculated in place.</summary>
        Archived = 3,

        /// <summary>Voided. Preserved for audit; never silently deleted.</summary>
        Cancelled = 4
    }
}
