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

        /// <summary>Active records for a set of employees within an inclusive range, in ONE query
        /// (for bulk operations — avoids a point-select per cell).</summary>
        IEnumerable<AttendanceRecord> GetByEmployeesAndDateRange(IEnumerable<long> employeeIds, DateTime from, DateTime to);

        long Insert(AttendanceRecord record);

        void Update(AttendanceRecord record);

        /// <summary>Inserts the day's record, or updates the existing active one for the same
        /// (employee, day) — a single statement using the partial unique index, no prior SELECT.
        /// Preserves the original CreatedAtUtc on update.</summary>
        void Upsert(AttendanceRecord record);

        void SoftDelete(long id);

        /// <summary>Soft-deletes many records by id in one operation (chunked under the SQLite
        /// variable limit) — for an atomic bulk erase.</summary>
        void SoftDeleteMany(IEnumerable<long> ids);
    }
}
