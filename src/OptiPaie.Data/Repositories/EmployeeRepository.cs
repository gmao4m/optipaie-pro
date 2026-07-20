using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="Employee"/>.</summary>
    internal sealed class EmployeeRepository : RepositoryBase, IEmployeeRepository
    {
        public EmployeeRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public Employee GetById(long id)
        {
            return Connection.QuerySingleOrDefault<Employee>(
                "SELECT * FROM Employees WHERE Id = @id;",
                new { id }, Transaction);
        }

        public IEnumerable<Employee> GetByCompany(long companyId, bool includeInactive = true)
        {
            return Connection.Query<Employee>(
                "SELECT * FROM Employees " +
                "WHERE CompanyId = @companyId AND IsDeleted = 0 AND (@inc = 1 OR IsActive = 1) " +
                "ORDER BY LastNameFr, FirstNameFr;",
                new { companyId, inc = includeInactive ? 1 : 0 }, Transaction);
        }

        public long Insert(Employee employee)
        {
            employee.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO Employees " +
                "(CompanyId, LastNameFr, LastNameAr, FirstNameFr, FirstNameAr, Gender, Nss, NationalId, " +
                " BirthDate, HireDate, ExitDate, Category, Poste, ContractType, MaritalStatus, Dependents, " +
                " BaseSalary, PaymentMode, Rib, IsActive, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @LastNameFr, @LastNameAr, @FirstNameFr, @FirstNameAr, @Gender, @Nss, @NationalId, " +
                " @BirthDate, @HireDate, @ExitDate, @Category, @Poste, @ContractType, @MaritalStatus, @Dependents, " +
                " @BaseSalary, @PaymentMode, @Rib, @IsActive, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, employee, Transaction);
            employee.Id = id;
            return id;
        }

        public void Update(Employee employee)
        {
            employee.UpdatedAtUtc = DateTime.UtcNow;

            const string sql =
                "UPDATE Employees SET " +
                "CompanyId = @CompanyId, LastNameFr = @LastNameFr, LastNameAr = @LastNameAr, " +
                "FirstNameFr = @FirstNameFr, FirstNameAr = @FirstNameAr, Gender = @Gender, Nss = @Nss, " +
                "NationalId = @NationalId, BirthDate = @BirthDate, HireDate = @HireDate, ExitDate = @ExitDate, " +
                "Category = @Category, Poste = @Poste, ContractType = @ContractType, MaritalStatus = @MaritalStatus, " +
                "Dependents = @Dependents, BaseSalary = @BaseSalary, " +
                "PaymentMode = @PaymentMode, Rib = @Rib, IsActive = @IsActive, UpdatedAtUtc = @UpdatedAtUtc, " +
                "IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, employee, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE Employees SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        public bool ExistsById(long id)
        {
            return Connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM Employees WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction) > 0;
        }
    }
}
