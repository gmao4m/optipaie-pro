namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle of a loan / salary advance.</summary>
    public enum LoanStatus
    {
        /// <summary>Being repaid — an instalment is due each month on the payslip.</summary>
        Active = 1,

        /// <summary>Fully repaid — reaches this automatically when the balance hits zero.</summary>
        Settled = 2,

        /// <summary>Temporarily paused — no instalment is deducted while suspended.</summary>
        Suspended = 3,

        /// <summary>Written off / cancelled — never deducted again.</summary>
        Cancelled = 4
    }
}
