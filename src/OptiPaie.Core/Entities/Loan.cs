using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A loan or salary advance granted to one employee. Always references the SHARED
    /// <c>Employees</c> table — no employee or company data is copied. The monthly
    /// instalment is fed into payroll as a deduction; what has actually been recovered
    /// lives in <see cref="LoanRepayment"/>, so the outstanding balance is always derived,
    /// never stored twice.
    /// </summary>
    public sealed class Loan : EntityBase
    {
        /// <summary>The shared employee who received the money.</summary>
        public long EmployeeId { get; set; }

        public LoanType Type { get; set; }

        public LoanStatus Status { get; set; }

        /// <summary>Total amount granted (principal).</summary>
        public decimal Principal { get; set; }

        /// <summary>Amount recovered each month on the payslip.</summary>
        public decimal MonthlyInstallment { get; set; }

        /// <summary>First period the instalment is due — year.</summary>
        public int StartYear { get; set; }

        /// <summary>First period the instalment is due — month (1-12).</summary>
        public int StartMonth { get; set; }

        /// <summary>Reason / reference given when the loan was granted.</summary>
        public string Reason { get; set; }

        public string Notes { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
