using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="EmployeeElement"/> assignments.</summary>
    internal sealed class EmployeeElementRepository : RepositoryBase, IEmployeeElementRepository
    {
        public EmployeeElementRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public EmployeeElement GetById(long id)
        {
            return Connection.QuerySingleOrDefault<EmployeeElement>(
                "SELECT * FROM EmployeeElements WHERE Id = @id;",
                new { id }, Transaction);
        }

        public IEnumerable<EmployeeElement> GetByEmployee(long employeeId, bool activeOnly = false)
        {
            return Connection.Query<EmployeeElement>(
                "SELECT * FROM EmployeeElements " +
                "WHERE EmployeeId = @employeeId AND (@activeOnly = 0 OR IsActive = 1) " +
                "ORDER BY Id;",
                new { employeeId, activeOnly = activeOnly ? 1 : 0 }, Transaction);
        }

        public EmployeeElement GetByEmployeeAndElement(long employeeId, long elementId)
        {
            return Connection.QuerySingleOrDefault<EmployeeElement>(
                "SELECT * FROM EmployeeElements WHERE EmployeeId = @employeeId AND ElementId = @elementId;",
                new { employeeId, elementId }, Transaction);
        }

        public long Insert(EmployeeElement assignment)
        {
            assignment.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO EmployeeElements " +
                "(EmployeeId, ElementId, Amount, Rate, Quantity, UnitPrice, IsActive, CreatedAtUtc) " +
                "VALUES " +
                "(@EmployeeId, @ElementId, @Amount, @Rate, @Quantity, @UnitPrice, @IsActive, @CreatedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, assignment, Transaction);
            assignment.Id = id;
            return id;
        }

        public void Update(EmployeeElement assignment)
        {
            const string sql =
                "UPDATE EmployeeElements SET " +
                "Amount = @Amount, Rate = @Rate, Quantity = @Quantity, UnitPrice = @UnitPrice, IsActive = @IsActive " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, assignment, Transaction);
        }

        public void Delete(long id)
        {
            Connection.Execute("DELETE FROM EmployeeElements WHERE Id = @id;", new { id }, Transaction);
        }
    }
}
