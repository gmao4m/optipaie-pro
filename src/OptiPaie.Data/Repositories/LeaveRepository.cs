using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="LeaveRequest"/>. Company-scoped queries join
    /// the shared Employees table rather than storing a company id here, so there is a
    /// single source of truth for who belongs to which company.
    /// </summary>
    internal sealed class LeaveRepository : RepositoryBase, ILeaveRepository
    {
        public LeaveRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public LeaveRequest GetById(long id)
        {
            return Connection.QuerySingleOrDefault<LeaveRequest>(
                "SELECT * FROM LeaveRequests WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public IEnumerable<LeaveRequest> GetByEmployee(long employeeId)
        {
            return Connection.Query<LeaveRequest>(
                "SELECT * FROM LeaveRequests WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "ORDER BY StartDate DESC;",
                new { employeeId }, Transaction);
        }

        public IEnumerable<LeaveRequest> GetByEmployeeRange(long employeeId, DateTime from, DateTime to)
        {
            // Overlap, not containment: any request touching the range is returned.
            return Connection.Query<LeaveRequest>(
                "SELECT * FROM LeaveRequests " +
                "WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "  AND StartDate <= @to AND EndDate >= @from " +
                "ORDER BY StartDate;",
                new { employeeId, from = SqliteDate.Day(from), to = SqliteDate.Day(to) }, Transaction);
        }

        public IEnumerable<LeaveRequest> GetByCompanyRange(long companyId, DateTime from, DateTime to)
        {
            return Connection.Query<LeaveRequest>(
                "SELECT l.* FROM LeaveRequests l " +
                "INNER JOIN Employees e ON e.Id = l.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND l.IsDeleted = 0 " +
                "  AND l.StartDate <= @to AND l.EndDate >= @from " +
                "ORDER BY l.StartDate DESC, e.LastNameFr, e.FirstNameFr;",
                new { companyId, from = SqliteDate.Day(from), to = SqliteDate.Day(to) }, Transaction);
        }

        public long Insert(LeaveRequest request)
        {
            request.CreatedAtUtc = DateTime.UtcNow;
            request.StartDate = SqliteDate.Day(request.StartDate);
            request.EndDate = SqliteDate.Day(request.EndDate);

            const string sql =
                "INSERT INTO LeaveRequests " +
                "(EmployeeId, Type, Status, StartDate, EndDate, Days, Reason, DecisionNote, " +
                " DecidedAtUtc, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @Type, @Status, @StartDate, @EndDate, @Days, @Reason, @DecisionNote, " +
                " @DecidedAtUtc, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, request, Transaction);
            request.Id = id;
            return id;
        }

        public void Update(LeaveRequest request)
        {
            request.UpdatedAtUtc = DateTime.UtcNow;
            request.StartDate = SqliteDate.Day(request.StartDate);
            request.EndDate = SqliteDate.Day(request.EndDate);

            const string sql =
                "UPDATE LeaveRequests SET " +
                "EmployeeId = @EmployeeId, Type = @Type, Status = @Status, " +
                "StartDate = @StartDate, EndDate = @EndDate, Days = @Days, Reason = @Reason, " +
                "DecisionNote = @DecisionNote, DecidedAtUtc = @DecidedAtUtc, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, request, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE LeaveRequests SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }
    }
}
