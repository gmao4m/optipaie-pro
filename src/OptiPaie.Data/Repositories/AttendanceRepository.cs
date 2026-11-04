using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="AttendanceRecord"/>. Company-scoped queries
    /// join the shared Employees table rather than storing a company id here, so
    /// there is a single source of truth for who belongs to which company.
    /// </summary>
    internal sealed class AttendanceRepository : RepositoryBase, IAttendanceRepository
    {
        public AttendanceRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public AttendanceRecord GetById(long id)
        {
            return Connection.QuerySingleOrDefault<AttendanceRecord>(
                "SELECT * FROM AttendanceRecords WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public AttendanceRecord GetByEmployeeAndDate(long employeeId, DateTime workDate)
        {
            return Connection.QuerySingleOrDefault<AttendanceRecord>(
                "SELECT * FROM AttendanceRecords " +
                "WHERE EmployeeId = @employeeId AND WorkDate = @workDate AND IsDeleted = 0;",
                new { employeeId, workDate = SqliteDate.Day(workDate) }, Transaction);
        }

        public IEnumerable<AttendanceRecord> GetByEmployeeRange(long employeeId, DateTime from, DateTime to)
        {
            return Connection.Query<AttendanceRecord>(
                "SELECT * FROM AttendanceRecords " +
                "WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "  AND WorkDate >= @from AND WorkDate <= @to " +
                "ORDER BY WorkDate;",
                new { employeeId, from = SqliteDate.Day(from), to = SqliteDate.Day(to) }, Transaction);
        }

        public IEnumerable<AttendanceRecord> GetByCompanyRange(long companyId, DateTime from, DateTime to)
        {
            return Connection.Query<AttendanceRecord>(
                "SELECT a.* FROM AttendanceRecords a " +
                "INNER JOIN Employees e ON e.Id = a.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND a.IsDeleted = 0 " +
                "  AND a.WorkDate >= @from AND a.WorkDate <= @to " +
                "ORDER BY a.WorkDate, e.LastNameFr, e.FirstNameFr;",
                new { companyId, from = SqliteDate.Day(from), to = SqliteDate.Day(to) }, Transaction);
        }

        public IEnumerable<AttendanceRecord> GetByEmployeesAndDateRange(IEnumerable<long> employeeIds, DateTime from, DateTime to)
        {
            List<long> ids = (employeeIds ?? Enumerable.Empty<long>()).Distinct().ToList();
            if (ids.Count == 0) return Enumerable.Empty<AttendanceRecord>();

            var result = new List<AttendanceRecord>();
            DateTime f = SqliteDate.Day(from), t = SqliteDate.Day(to);
            // Chunk the IN list under SQLite's variable limit (defensive; companies rarely exceed it).
            for (int i = 0; i < ids.Count; i += 900)
            {
                List<long> chunk = ids.Skip(i).Take(900).ToList();
                result.AddRange(Connection.Query<AttendanceRecord>(
                    "SELECT * FROM AttendanceRecords " +
                    "WHERE EmployeeId IN @ids AND IsDeleted = 0 AND WorkDate >= @f AND WorkDate <= @t;",
                    new { ids = chunk, f, t }, Transaction));
            }
            return result;
        }

        public long Insert(AttendanceRecord record)
        {
            record.CreatedAtUtc = DateTime.UtcNow;
            record.WorkDate = SqliteDate.Day(record.WorkDate);

            const string sql =
                "INSERT INTO AttendanceRecords " +
                "(EmployeeId, WorkDate, Status, CheckIn, CheckOut, WorkedHours, LateMinutes, " +
                " OvertimeHours, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @WorkDate, @Status, @CheckIn, @CheckOut, @WorkedHours, @LateMinutes, " +
                " @OvertimeHours, @Notes, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, record, Transaction);
            record.Id = id;
            return id;
        }

        public void Update(AttendanceRecord record)
        {
            record.UpdatedAtUtc = DateTime.UtcNow;
            record.WorkDate = SqliteDate.Day(record.WorkDate);

            const string sql =
                "UPDATE AttendanceRecords SET " +
                "EmployeeId = @EmployeeId, WorkDate = @WorkDate, Status = @Status, " +
                "CheckIn = @CheckIn, CheckOut = @CheckOut, WorkedHours = @WorkedHours, " +
                "LateMinutes = @LateMinutes, OvertimeHours = @OvertimeHours, Notes = @Notes, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, record, Transaction);
        }

        public void Upsert(AttendanceRecord record)
        {
            record.WorkDate = SqliteDate.Day(record.WorkDate);
            record.CreatedAtUtc = DateTime.UtcNow;   // used only if this is an insert
            record.UpdatedAtUtc = DateTime.UtcNow;

            // One statement: insert, or — on the (EmployeeId, WorkDate) partial unique index — update
            // the existing ACTIVE row. No prior SELECT. CreatedAtUtc is NOT in the DO UPDATE set, so
            // the original creation time is preserved. IsDeleted is forced 0 (an active record).
            const string sql =
                "INSERT INTO AttendanceRecords " +
                "(EmployeeId, WorkDate, Status, CheckIn, CheckOut, WorkedHours, LateMinutes, " +
                " OvertimeHours, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @WorkDate, @Status, @CheckIn, @CheckOut, @WorkedHours, @LateMinutes, " +
                " @OvertimeHours, @Notes, @CreatedAtUtc, @UpdatedAtUtc, 0) " +
                "ON CONFLICT(EmployeeId, WorkDate) WHERE IsDeleted = 0 DO UPDATE SET " +
                "  Status = excluded.Status, CheckIn = excluded.CheckIn, CheckOut = excluded.CheckOut, " +
                "  WorkedHours = excluded.WorkedHours, LateMinutes = excluded.LateMinutes, " +
                "  OvertimeHours = excluded.OvertimeHours, Notes = excluded.Notes, " +
                "  UpdatedAtUtc = excluded.UpdatedAtUtc;";

            Connection.Execute(sql, record, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE AttendanceRecords SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        public void SoftDeleteMany(IEnumerable<long> ids)
        {
            List<long> all = (ids ?? Enumerable.Empty<long>()).Distinct().ToList();
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < all.Count; i += 900)
            {
                List<long> chunk = all.Skip(i).Take(900).ToList();
                Connection.Execute(
                    "UPDATE AttendanceRecords SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id IN @ids;",
                    new { ids = chunk, now }, Transaction);
            }
        }
    }
}
