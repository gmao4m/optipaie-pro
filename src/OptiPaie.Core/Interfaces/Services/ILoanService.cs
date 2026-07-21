using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Loan / salary-advance operations. Owns the balance and instalment rules and the
    /// payroll integration: the amount due for a period is exposed for the payslip, and
    /// a payroll run records the recovery so a loan is repaid exactly once per month.
    /// </summary>
    public interface ILoanService
    {
        /// <summary>Creates or updates a loan (limited edits once it has repayments).</summary>
        Result<long> Save(Loan loan);

        /// <summary>Suspends, resumes or cancels a loan.</summary>
        Result SetStatus(long id, Core.Enums.LoanStatus status);

        /// <summary>Soft-deletes a loan and its repayments.</summary>
        Result Delete(long id);

        Loan Get(long id);

        LoanSummary GetSummary(long loanId);

        /// <summary>Loans of one employee with their derived positions.</summary>
        IReadOnlyList<LoanSummary> GetByEmployee(long employeeId);

        /// <summary>Loans of a whole company with their derived positions.</summary>
        IReadOnlyList<LoanSummary> GetByCompany(long companyId);

        /// <summary>Recorded and manual repayments of one loan, oldest first.</summary>
        IReadOnlyList<LoanRepayment> GetRepayments(long loanId);

        /// <summary>Records a manual repayment (outside a payroll run).</summary>
        Result AddManualRepayment(long loanId, int year, int month, decimal amount);

        /// <summary>Removes a repayment and reopens the loan if it was settled.</summary>
        Result RemoveRepayment(long repaymentId);

        /// <summary>
        /// Total loan instalment to deduct on one employee's payslip for a period.
        /// Reproducible: if a period was already recorded, its recorded amount is
        /// returned; otherwise the scheduled instalment (capped at the balance).
        /// Read-only — records nothing.
        /// </summary>
        decimal GetMonthlyDeduction(long employeeId, int year, int month);

        /// <summary>
        /// Records the period's recovery for every active loan of the employee, once.
        /// Idempotent per period and safe to call after each payroll generation.
        /// Returns the total recovered.
        /// </summary>
        Result<decimal> RecordPayrollDeductions(long employeeId, int year, int month);

        /// <summary>Amount an employee still owes across every active loan.</summary>
        decimal GetOutstanding(long employeeId);
    }
}
