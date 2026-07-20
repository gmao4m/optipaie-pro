using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="PayrollDetail"/> lines.</summary>
    public interface IPayrollDetailRepository
    {
        /// <summary>Returns the detail lines of a payslip, in display order.</summary>
        IEnumerable<PayrollDetail> GetByPayslip(long payslipId);

        /// <summary>Inserts a detail line and returns its new id.</summary>
        long Insert(PayrollDetail detail);

        /// <summary>Deletes all detail lines of a payslip.</summary>
        void DeleteByPayslip(long payslipId);
    }
}
