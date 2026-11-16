using System.Collections.Generic;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A printable loan ledger over a period, for one employee or a whole company:
    /// what was lent, what has been recovered (all-time and within the window), and
    /// what remains. Every figure is derived from the recorded repayments — nothing is
    /// stored twice — so the report can never disagree with the loans screen.
    /// </summary>
    public sealed class LoanLedgerReport
    {
        /// <summary>Company whose loans are reported (for the header / isolation).</summary>
        public long CompanyId { get; set; }

        /// <summary>Company display name.</summary>
        public string CompanyName { get; set; }

        /// <summary>Employee name when the report is scoped to one person; null for the whole company.</summary>
        public string EmployeeName { get; set; }

        public int FromYear { get; set; }
        public int FromMonth { get; set; }
        public int ToYear { get; set; }
        public int ToMonth { get; set; }

        public IReadOnlyList<LoanLedgerLine> Lines { get; set; } = new List<LoanLedgerLine>();

        /// <summary>Total principal of the loans shown.</summary>
        public decimal TotalLent { get; set; }

        /// <summary>Total recovered within the window.</summary>
        public decimal TotalRecoveredInPeriod { get; set; }

        /// <summary>Total recovered all-time on the loans shown.</summary>
        public decimal TotalRecovered { get; set; }

        /// <summary>Total still outstanding now.</summary>
        public decimal TotalRemaining { get; set; }

        /// <summary>Loans flagged late (an issued-payroll month skipped the instalment).</summary>
        public int LateCount { get; set; }
    }

    /// <summary>One loan's line in a <see cref="LoanLedgerReport"/>.</summary>
    public sealed class LoanLedgerLine
    {
        public long LoanId { get; set; }
        public string EmployeeName { get; set; }
        public LoanType Type { get; set; }
        public LoanStatus Status { get; set; }

        /// <summary>Principal lent.</summary>
        public decimal Lent { get; set; }

        /// <summary>Recovered within the reporting window.</summary>
        public decimal RecoveredInPeriod { get; set; }

        /// <summary>Recovered all-time.</summary>
        public decimal RecoveredTotal { get; set; }

        /// <summary>Still outstanding now.</summary>
        public decimal Remaining { get; set; }

        public bool IsLate { get; set; }
        public int MissedInstallments { get; set; }
    }
}
