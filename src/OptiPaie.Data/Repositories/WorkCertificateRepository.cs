using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="WorkCertificate"/>. Company-scoped queries join
    /// the shared Employees table rather than storing a company id here.
    /// </summary>
    internal sealed class WorkCertificateRepository : RepositoryBase, IWorkCertificateRepository
    {
        public WorkCertificateRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public WorkCertificate GetById(long id)
        {
            return Connection.QuerySingleOrDefault<WorkCertificate>(
                "SELECT * FROM WorkCertificates WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public IEnumerable<WorkCertificate> GetByEmployee(long employeeId)
        {
            return Connection.Query<WorkCertificate>(
                "SELECT * FROM WorkCertificates WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "ORDER BY IssueDate DESC, Id DESC;",
                new { employeeId }, Transaction);
        }

        public IEnumerable<WorkCertificate> GetByCompany(long companyId)
        {
            return Connection.Query<WorkCertificate>(
                "SELECT w.* FROM WorkCertificates w " +
                "INNER JOIN Employees e ON e.Id = w.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND w.IsDeleted = 0 " +
                "ORDER BY w.IssueDate DESC, w.Id DESC;",
                new { companyId }, Transaction);
        }

        public int CountForCompanyYear(long companyId, int year)
        {
            string from = new DateTime(year, 1, 1).ToString("yyyy-MM-dd");
            string toExclusive = new DateTime(year + 1, 1, 1).ToString("yyyy-MM-dd");

            return Connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM WorkCertificates w " +
                "INNER JOIN Employees e ON e.Id = w.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND w.IsDeleted = 0 " +
                "  AND w.IssueDate >= @from AND w.IssueDate < @toExclusive;",
                new { companyId, from, toExclusive }, Transaction);
        }

        public long Insert(WorkCertificate certificate)
        {
            certificate.CreatedAtUtc = DateTime.UtcNow;
            certificate.IssueDate = SqliteDate.Day(certificate.IssueDate);

            const string sql =
                "INSERT INTO WorkCertificates (EmployeeId, Type, Reference, IssueDate, Purpose, Body, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES (@EmployeeId, @Type, @Reference, @IssueDate, @Purpose, @Body, " +
                " @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, certificate, Transaction);
            certificate.Id = id;
            return id;
        }

        public void Update(WorkCertificate certificate)
        {
            certificate.UpdatedAtUtc = DateTime.UtcNow;
            certificate.IssueDate = SqliteDate.Day(certificate.IssueDate);

            const string sql =
                "UPDATE WorkCertificates SET " +
                "EmployeeId = @EmployeeId, Type = @Type, Reference = @Reference, IssueDate = @IssueDate, " +
                "Purpose = @Purpose, Body = @Body, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, certificate, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE WorkCertificates SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }
    }
}
