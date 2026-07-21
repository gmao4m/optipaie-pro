namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle of a leave request.</summary>
    public enum LeaveStatus
    {
        /// <summary>Submitted, awaiting a decision. Reserves nothing yet.</summary>
        Pending = 1,

        /// <summary>Approved — the days are written into attendance automatically.</summary>
        Approved = 2,

        /// <summary>Refused. Never consumes the balance.</summary>
        Rejected = 3,

        /// <summary>Withdrawn after approval — the attendance days are removed again.</summary>
        Cancelled = 4
    }
}
