using System;
using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Attendance module operations. All calculations (worked hours, lateness,
    /// overtime) are derived here from the configured settings, so every screen and
    /// the payroll integration see identical values.
    /// </summary>
    public interface IAttendanceService
    {
        /// <summary>
        /// Creates or updates the day of one employee (one record per employee/day)
        /// and recomputes the derived hours, lateness and overtime.
        /// </summary>
        Result<long> Save(AttendanceRecord record);

        /// <summary>Saves a whole day for several employees atomically.</summary>
        Result SaveMany(IEnumerable<AttendanceRecord> records);

        /// <summary>Removes a record (soft delete).</summary>
        Result Delete(long id);

        /// <summary>The record of one employee on one day, or null.</summary>
        AttendanceRecord Get(long employeeId, DateTime workDate);

        /// <summary>Every record of a company on one day.</summary>
        IReadOnlyList<AttendanceRecord> GetCompanyDay(long companyId, DateTime workDate);

        /// <summary>Monthly totals for one employee — consumed by payroll.</summary>
        AttendanceSummary GetMonthlySummary(long employeeId, int year, int month);

        /// <summary>Monthly totals for every employee of a company.</summary>
        IReadOnlyList<AttendanceSummary> GetCompanyMonthlySummary(long companyId, int year, int month);

        /// <summary>Current module settings (start time, standard hours, tolerance).</summary>
        AttendanceSettings GetSettings();

        /// <summary>Persists the module settings.</summary>
        Result SaveSettings(AttendanceSettings settings);
    }
}
