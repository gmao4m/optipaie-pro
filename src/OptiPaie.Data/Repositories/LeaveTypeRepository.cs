using System.Collections.Generic;
using System.Data;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper persistence for the configurable <see cref="LeaveTypeDefinition"/> catalogue.</summary>
    internal sealed class LeaveTypeRepository : RepositoryBase, ILeaveTypeRepository
    {
        public LeaveTypeRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public LeaveTypeDefinition GetById(long id)
        {
            return Connection.QuerySingleOrDefault<LeaveTypeDefinition>(
                "SELECT * FROM LeaveTypes WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);
        }

        public IEnumerable<LeaveTypeDefinition> GetForCompany(long companyId)
        {
            // The company's own types plus the global defaults (CompanyId IS NULL).
            return Connection.Query<LeaveTypeDefinition>(
                "SELECT * FROM LeaveTypes " +
                "WHERE IsDeleted = 0 AND (CompanyId IS NULL OR CompanyId = @companyId) " +
                "ORDER BY SortOrder, Id;",
                new { companyId }, Transaction);
        }

        public long Insert(LeaveTypeDefinition type)
        {
            type.CreatedAtUtc = System.DateTime.UtcNow;

            const string sql =
                "INSERT INTO LeaveTypes " +
                "(CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, " +
                " LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @Code, @LabelAr, @LabelFr, @BaseType, @PaymentCategory, @DecrementsAnnualBalance, " +
                " @LegalDurationDays, @OncePerCareer, @IsActive, @SortOrder, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, type, Transaction);
            type.Id = id;
            return id;
        }

        public void Update(LeaveTypeDefinition type)
        {
            type.UpdatedAtUtc = System.DateTime.UtcNow;

            const string sql =
                "UPDATE LeaveTypes SET " +
                "CompanyId = @CompanyId, Code = @Code, LabelAr = @LabelAr, LabelFr = @LabelFr, " +
                "BaseType = @BaseType, PaymentCategory = @PaymentCategory, " +
                "DecrementsAnnualBalance = @DecrementsAnnualBalance, LegalDurationDays = @LegalDurationDays, " +
                "OncePerCareer = @OncePerCareer, IsActive = @IsActive, SortOrder = @SortOrder, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, type, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE LeaveTypes SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = System.DateTime.UtcNow }, Transaction);
        }
    }
}
