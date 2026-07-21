namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// An employee's annual-leave position for a year, derived from the shared
    /// employee record (hire date) and the approved requests. Never stored — always
    /// computed, so it cannot drift out of date.
    /// </summary>
    public sealed class LeaveBalance
    {
        public long EmployeeId { get; set; }

        /// <summary>Display name (filled when listing a whole company).</summary>
        public string EmployeeName { get; set; }

        public int Year { get; set; }

        /// <summary>Days earned: 2,5 per month worked in the year, capped at 30.</summary>
        public decimal Entitlement { get; set; }

        /// <summary>Approved annual-leave days for the year.</summary>
        public decimal Taken { get; set; }

        /// <summary>Approved annual-leave days still awaiting a decision.</summary>
        public decimal Pending { get; set; }

        /// <summary>Entitlement − taken (pending is shown separately, not deducted).</summary>
        public decimal Remaining { get; set; }

        /// <summary>Approved days of every other type (sick, unpaid, maternity, special).</summary>
        public decimal OtherLeaveDays { get; set; }

        /// <summary>Approved UNPAID days — the ones payroll deducts.</summary>
        public decimal UnpaidDays { get; set; }
    }

    /// <summary>Module settings driving the leave calculations.</summary>
    public sealed class LeaveSettings
    {
        /// <summary>Days earned per month worked (loi 90-11 art. 41: 2,5).</summary>
        public decimal DaysPerMonth { get; set; } = 2.5m;

        /// <summary>Yearly cap on annual leave (30 days).</summary>
        public decimal AnnualCap { get; set; } = 30m;

        /// <summary>Whether Friday/Saturday (the Algerian weekend) are excluded from the count.</summary>
        public bool ExcludeRestDays { get; set; } = true;
    }
}
