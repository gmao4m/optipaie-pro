using System;
using System.Collections.Generic;
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
                new { employeeId, workDate = workDate.Date }, Transaction);
        }

        public IEnumerable<AttendanceRecord> GetByEmployeeRange(long employeeId, DateTime from, DateTime to)
        {
            return Connection.Query<AttendanceRecord>(
                "SELECT * FROM AttendanceRecords " +
                "WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "  AND WorkDate >= @from AND WorkDate <= @to " +
                "ORDER BY WorkDate;",
                new { employeeId, from = from.Date, to = to.Date }, Transaction);
        }

        public IEnumerable<AttendanceRecord> GetByCompanyRange(long companyId, DateTime from, DateTime to)
        {
            return Connection.Query<AttendanceRecord>(
                "SELECT a.* FROM AttendanceRecords a " +
                "INNER JOIN Employees e ON e.Id = a.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND a.IsDeleted = 0 " +
                "  AND a.WorkDate >= @from AND a.WorkDate <= @to " +
                "ORDER BY a.WorkDate, e.LastNameFr, e.FirstNameFr;",
                new { companyId, from = from.Date, to = to.Date }, Transaction);
        }

        public long Insert(AttendanceRecord record)
        {
            record.CreatedAtUtc = DateTime.UtcNow;
            record.WorkDate = record.WorkDate.Date;

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
            record.WorkDate = record.WorkDate.Date;

            const string sql =
                "UPDATE AttendanceRecords SET " +
                "EmployeeId = @EmployeeId, WorkDate = @WorkDate, Status = @Status, " +
                "CheckIn = @CheckIn, CheckOut = @CheckOut, WorkedHours = @WorkedHours, " +
                "LateMinutes = @LateMinutes, OvertimeHours = @OvertimeHours, Notes = @Notes, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, record, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE AttendanceRecords SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }
    }
}
