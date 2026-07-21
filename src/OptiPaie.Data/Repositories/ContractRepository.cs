using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="EmploymentContract"/>. Company-scoped queries
    /// join the shared Employees table rather than storing a company id here, so there
    /// is a single source of truth for who belongs to which company.
    /// </summary>
    internal sealed class ContractRepository : RepositoryBase, IContractRepository
    {
        private const int StatusActive = 2;

        public ContractRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public EmploymentContract GetById(long id)
        {
            return Connection.QuerySingleOrDefault<EmploymentContract>(
                "SELECT * FROM EmploymentContracts WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public IEnumerable<EmploymentContract> GetByEmployee(long employeeId)
        {
            return Connection.Query<EmploymentContract>(
                "SELECT * FROM EmploymentContracts WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "ORDER BY StartDate DESC, Id DESC;",
                new { employeeId }, Transaction);
        }

        public EmploymentContract GetActiveForEmployee(long employeeId)
        {
            return Connection.QuerySingleOrDefault<EmploymentContract>(
                "SELECT * FROM EmploymentContracts " +
                "WHERE EmployeeId = @employeeId AND Status = @status AND IsDeleted = 0 " +
                "ORDER BY StartDate DESC LIMIT 1;",
                new { employeeId, status = StatusActive }, Transaction);
        }

        public IEnumerable<EmploymentContract> GetByCompany(long companyId)
        {
            return Connection.Query<EmploymentContract>(
                "SELECT c.* FROM EmploymentContracts c " +
                "INNER JOIN Employees e ON e.Id = c.EmployeeId " +
                "WHERE e.CompanyId = @companyId AND e.IsDeleted = 0 AND c.IsDeleted = 0 " +
                "ORDER BY c.Status, e.LastNameFr, e.FirstNameFr, c.StartDate DESC;",
                new { companyId }, Transaction);
        }

        public long Insert(EmploymentContract contract)
        {
            contract.CreatedAtUtc = DateTime.UtcNow;
            contract.StartDate = SqliteDate.Day(contract.StartDate);
            if (contract.EndDate.HasValue) contract.EndDate = SqliteDate.Day(contract.EndDate.Value);

            const string sql =
                "INSERT INTO EmploymentContracts " +
                "(EmployeeId, Type, Status, Reference, Position, BaseSalary, StartDate, EndDate, " +
                " TrialPeriodDays, PreviousContractId, SignedDate, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@EmployeeId, @Type, @Status, @Reference, @Position, @BaseSalary, @StartDate, @EndDate, " +
                " @TrialPeriodDays, @PreviousContractId, @SignedDate, @Notes, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, contract, Transaction);
            contract.Id = id;
            return id;
        }

        public void Update(EmploymentContract contract)
        {
            contract.UpdatedAtUtc = DateTime.UtcNow;
            contract.StartDate = SqliteDate.Day(contract.StartDate);
            if (contract.EndDate.HasValue) contract.EndDate = SqliteDate.Day(contract.EndDate.Value);

            const string sql =
                "UPDATE EmploymentContracts SET " +
                "EmployeeId = @EmployeeId, Type = @Type, Status = @Status, Reference = @Reference, " +
                "Position = @Position, BaseSalary = @BaseSalary, StartDate = @StartDate, EndDate = @EndDate, " +
                "TrialPeriodDays = @TrialPeriodDays, PreviousContractId = @PreviousContractId, " +
                "SignedDate = @SignedDate, Notes = @Notes, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, contract, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE EmploymentContracts SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }
    }
}
