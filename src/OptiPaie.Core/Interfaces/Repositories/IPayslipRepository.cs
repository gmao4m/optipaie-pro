using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="Payslip"/>.</summary>
    public interface IPayslipRepository
    {
        /// <summary>Returns the payslip with the given id regardless of status, or null.</summary>
        Payslip GetById(long id);

        /// <summary>Returns the ACTIVE payslips of a run. Superseded/cancelled rows are excluded,
        /// so every live consumer (declarations, archive, duplicate checks) sees only valid payslips.</summary>
        IEnumerable<Payslip> GetByRun(long runId);

        /// <summary>Returns the ACTIVE payslips of an employee, newest first.</summary>
        IEnumerable<Payslip> GetByEmployee(long employeeId);

        /// <summary>Returns the non-active (superseded/cancelled) payslips of a run, for the history view.</summary>
        IEnumerable<Payslip> GetInactiveByRun(long runId);

        /// <summary>True when the employee has ANY payslip (any status) — the CNAS-history guard that
        /// prevents deleting an employee who was ever declared, even if their payslips were cancelled.</summary>
        bool HasAnyByEmployee(long employeeId);

        /// <summary>Inserts a payslip and returns its new id.</summary>
        long Insert(Payslip payslip);

        /// <summary>Updates only the lifecycle fields (Status + revision metadata) of a payslip.</summary>
        void UpdateStatus(Payslip payslip);

        /// <summary>Deletes all payslips of a run (used when recomputing a draft run).</summary>
        void DeleteByRun(long runId);
    }
}
