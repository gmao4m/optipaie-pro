using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="Department"/>.</summary>
    internal sealed class DepartmentRepository : RepositoryBase, IDepartmentRepository
    {
        public DepartmentRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public IEnumerable<Department> GetByCompany(long companyId)
        {
            return Connection.Query<Department>(
                "SELECT * FROM Departments WHERE CompanyId = @companyId AND IsDeleted = 0 " +
                "ORDER BY DisplayOrder, Name;",
                new { companyId }, Transaction);
        }

        public Department GetById(long id)
        {
            return Connection.QuerySingleOrDefault<Department>(
                "SELECT * FROM Departments WHERE Id = @id;", new { id }, Transaction);
        }

        public long Insert(Department department)
        {
            department.CreatedAtUtc = DateTime.UtcNow;
            const string sql =
                "INSERT INTO Departments (CompanyId, Name, DisplayOrder, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES (@CompanyId, @Name, @DisplayOrder, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";
            department.Id = Connection.ExecuteScalar<long>(sql, department, Transaction);
            return department.Id;
        }

        public void Update(Department department)
        {
            department.UpdatedAtUtc = DateTime.UtcNow;
            const string sql =
                "UPDATE Departments SET Name = @Name, DisplayOrder = @DisplayOrder, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted WHERE Id = @Id;";
            Connection.Execute(sql, department, Transaction);
        }
    }
}
