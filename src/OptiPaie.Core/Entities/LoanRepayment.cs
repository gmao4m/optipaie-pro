using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One monthly recovery of a <see cref="Loan"/>. Exactly one per (loan, period),
    /// so a payslip can never deduct the same instalment twice. Recorded when payroll
    /// is actually generated for the period — a preview records nothing.
    /// </summary>
    public sealed class LoanRepayment : EntityBase
    {
        public long LoanId { get; set; }

        /// <summary>Period year.</summary>
        public int Year { get; set; }

        /// <summary>Period month (1-12).</summary>
        public int Month { get; set; }

        /// <summary>Amount recovered on that period's payslip.</summary>
        public decimal Amount { get; set; }

        /// <summary>True for a repayment entered by hand rather than by a payroll run.</summary>
        public bool IsManual { get; set; }

        /// <summary>
        /// The payslip that actually deducted this instalment, or null. Set by a payroll run
        /// (migration 0042 back-fills historical rows from the issued bulletins); always null for
        /// a manual repayment, which was never tied to a run.
        /// </summary>
        public long? PayslipId { get; set; }

        public bool IsDeleted { get; set; }
    }
}
