using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for loans and their repayments. Company-scoped queries join the
    /// shared Employees table — the company is never stored on the loan itself.
    /// </summary>
    public interface ILoanRepository
    {
        Loan GetById(long id);

        /// <summary>Loans of one employee, most recent first.</summary>
        IEnumerable<Loan> GetByEmployee(long employeeId);

        /// <summary>Loans of a whole company.</summary>
        IEnumerable<Loan> GetByCompany(long companyId);

        long Insert(Loan loan);

        void Update(Loan loan);

        void SoftDelete(long id);

        // -- repayments --------------------------------------------------------

        /// <summary>Every repayment of one loan, oldest first.</summary>
        IEnumerable<LoanRepayment> GetRepayments(long loanId);

        /// <summary>The repayment of one loan for a given period, or null.</summary>
        LoanRepayment GetRepayment(long loanId, int year, int month);

        /// <summary>A repayment by its id (carries its owning LoanId), or null.</summary>
        LoanRepayment GetRepaymentById(long id);

        long InsertRepayment(LoanRepayment repayment);

        void SoftDeleteRepayment(long id);
    }
}
