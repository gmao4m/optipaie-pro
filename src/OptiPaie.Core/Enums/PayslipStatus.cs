namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// Lifecycle state of a single <see cref="Entities.Payslip"/>. The value is stored
    /// in the database (migration 0036) and the "one per employee per run" unique index
    /// applies to <see cref="Active"/> payslips only, so a corrected version can coexist
    /// with its superseded original and a cancelled slip frees its period slot.
    /// </summary>
    public enum PayslipStatus
    {
        /// <summary>The current, valid payslip. The only state read by declarations,
        /// the archive list and every downstream artifact.</summary>
        Active = 1,

        /// <summary>Replaced by a corrected version. Kept for audit; never declared or
        /// reprinted as the valid bulletin.</summary>
        Superseded = 2,

        /// <summary>Voided. Preserved for audit and restorable; excluded everywhere and
        /// never reprintable as a valid bulletin.</summary>
        Cancelled = 3
    }
}
