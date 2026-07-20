using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="Payslip"/>.</summary>
    public interface IPayslipRepository
    {
        /// <summary>Returns the payslip with the given id, or null.</summary>
        Payslip GetById(long id);

        /// <summary>Returns the payslips of a run.</summary>
        IEnumerable<Payslip> GetByRun(long runId);

        /// <summary>Returns the payslips of an employee, newest first.</summary>
        IEnumerable<Payslip> GetByEmployee(long employeeId);

        /// <summary>Inserts a payslip and returns its new id.</summary>
        long Insert(Payslip payslip);

        /// <summary>Deletes all payslips of a run (used when recomputing a draft run).</summary>
        void DeleteByRun(long runId);
    }
}
