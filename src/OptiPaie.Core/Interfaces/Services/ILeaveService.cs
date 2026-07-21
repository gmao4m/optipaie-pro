using System;
using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Leave module operations. Owns the day counting, the overlap rules and the
    /// balance calculation, and keeps the Attendance module in sync automatically:
    /// approving a request writes its days as attendance, cancelling removes them.
    /// </summary>
    public interface ILeaveService
    {
        /// <summary>Creates or updates a request (only while it is still pending).</summary>
        Result<long> Save(LeaveRequest request);

        /// <summary>
        /// Approves a request and writes its days into attendance (paid types as
        /// "Congé", unpaid as "Absent" so payroll deducts them).
        /// </summary>
        Result Approve(long id, string note);

        /// <summary>Rejects a pending request. Consumes nothing.</summary>
        Result Reject(long id, string note);

        /// <summary>Cancels an approved request and removes the attendance days again.</summary>
        Result Cancel(long id, string note);

        /// <summary>Soft-deletes a request (and its attendance days when approved).</summary>
        Result Delete(long id);

        LeaveRequest Get(long id);

        /// <summary>Every request of one employee, most recent first.</summary>
        IReadOnlyList<LeaveRequest> GetByEmployee(long employeeId);

        /// <summary>Requests of a company overlapping the given year.</summary>
        IReadOnlyList<LeaveRequest> GetByCompanyYear(long companyId, int year);

        /// <summary>Annual-leave position of one employee for a year.</summary>
        LeaveBalance GetBalance(long employeeId, int year);

        /// <summary>Annual-leave position of every employee of a company.</summary>
        IReadOnlyList<LeaveBalance> GetCompanyBalances(long companyId, int year);

        /// <summary>Leave days counted between two dates, rest days excluded.</summary>
        decimal CountDays(DateTime start, DateTime end);

        LeaveSettings GetSettings();

        Result SaveSettings(LeaveSettings settings);
    }
}
