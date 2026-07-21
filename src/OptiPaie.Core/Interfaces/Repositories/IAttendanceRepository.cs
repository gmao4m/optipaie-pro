using System;
using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence for <see cref="AttendanceRecord"/> (shared employees).</summary>
    public interface IAttendanceRepository
    {
        AttendanceRecord GetById(long id);

        /// <summary>The record of one employee on one day, or null.</summary>
        AttendanceRecord GetByEmployeeAndDate(long employeeId, DateTime workDate);

        /// <summary>All records of one employee within an inclusive date range.</summary>
        IEnumerable<AttendanceRecord> GetByEmployeeRange(long employeeId, DateTime from, DateTime to);

        /// <summary>All records of a company's employees within an inclusive date range.</summary>
        IEnumerable<AttendanceRecord> GetByCompanyRange(long companyId, DateTime from, DateTime to);

        long Insert(AttendanceRecord record);

        void Update(AttendanceRecord record);

        void SoftDelete(long id);
    }
}
