using System;
using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for leave requests. Company-scoped queries join the shared
    /// Employees table — the company is never stored on the request itself.
    /// </summary>
    public interface ILeaveRepository
    {
        LeaveRequest GetById(long id);

        /// <summary>Every request of one employee, most recent first.</summary>
        IEnumerable<LeaveRequest> GetByEmployee(long employeeId);

        /// <summary>Requests of one employee overlapping a date range.</summary>
        IEnumerable<LeaveRequest> GetByEmployeeRange(long employeeId, DateTime from, DateTime to);

        /// <summary>Requests of a whole company overlapping a date range.</summary>
        IEnumerable<LeaveRequest> GetByCompanyRange(long companyId, DateTime from, DateTime to);

        long Insert(LeaveRequest request);

        void Update(LeaveRequest request);

        void SoftDelete(long id);
    }
}
