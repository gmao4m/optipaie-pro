namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A loan with its derived position. The outstanding balance is always computed
    /// from the recorded repayments, never stored, so it cannot drift out of date.
    /// </summary>
    public sealed class LoanSummary
    {
        public long LoanId { get; set; }
        public long EmployeeId { get; set; }

        /// <summary>Display name (filled when listing a whole company).</summary>
        public string EmployeeName { get; set; }

        /// <summary>Lifecycle state (for dashboards / filtering).</summary>
        public OptiPaie.Core.Enums.LoanStatus Status { get; set; }

        public decimal Principal { get; set; }
        public decimal MonthlyInstallment { get; set; }

        /// <summary>Sum of the recorded repayments.</summary>
        public decimal Repaid { get; set; }

        /// <summary>Principal − repaid (never below zero).</summary>
        public decimal Outstanding { get; set; }

        /// <summary>Whole instalments still to run (the last one may be smaller).</summary>
        public int RemainingInstallments { get; set; }

        /// <summary>
        /// True when an instalment was due in a month whose payroll was ALREADY ISSUED and it was
        /// not deducted — money that should have been recovered and was not. Only an Active loan
        /// with a positive balance can be late.
        /// </summary>
        public bool IsLate { get; set; }

        /// <summary>How many issued-payroll months skipped this loan's instalment.</summary>
        public int MissedInstallments { get; set; }

        /// <summary>Approximate amount not yet recovered because of those misses (capped at the balance).</summary>
        public decimal MissedAmount { get; set; }
    }
}
