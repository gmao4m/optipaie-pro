using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="PayrollElement"/>.</summary>
    internal sealed class PayrollElementRepository : RepositoryBase, IPayrollElementRepository
    {
        public PayrollElementRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public PayrollElement GetById(long id)
        {
            return Connection.QuerySingleOrDefault<PayrollElement>(
                "SELECT * FROM PayrollElements WHERE Id = @id;",
                new { id }, Transaction);
        }

        public IEnumerable<PayrollElement> GetAll(bool includeDisabled = true, bool includeDeleted = false)
        {
            return Connection.Query<PayrollElement>(
                "SELECT * FROM PayrollElements " +
                "WHERE (@allDeleted = 1 OR IsDeleted = 0) AND (@allDisabled = 1 OR IsEnabled = 1) " +
                "ORDER BY DisplayOrder, NameFr;",
                new { allDeleted = includeDeleted ? 1 : 0, allDisabled = includeDisabled ? 1 : 0 }, Transaction);
        }

        public long Insert(PayrollElement element)
        {
            element.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO PayrollElements " +
                "(NameFr, NameAr, Description, ElementType, CalculationMethod, CalculationBase, DefaultAmount, " +
                " DefaultRate, DefaultQuantity, DefaultUnitPrice, Periodicity, IsCnasApplicable, IsIrgApplicable, " +
                " CnasPercent, IrgPercent, " +
                " IsIncludedInGross, ExemptionCeiling, IsEditable, IsEnabled, IsSystem, DisplayOrder, " +
                " InternalCode, IsPrintable, IncludedInLissage, IsAutomatic, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@NameFr, @NameAr, @Description, @ElementType, @CalculationMethod, @CalculationBase, @DefaultAmount, " +
                " @DefaultRate, @DefaultQuantity, @DefaultUnitPrice, @Periodicity, @IsCnasApplicable, @IsIrgApplicable, " +
                " @CnasPercent, @IrgPercent, " +
                " @IsIncludedInGross, @ExemptionCeiling, @IsEditable, @IsEnabled, @IsSystem, @DisplayOrder, " +
                " @InternalCode, @IsPrintable, @IncludedInLissage, @IsAutomatic, " +
                " @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, element, Transaction);
            element.Id = id;
            return id;
        }

        public void Update(PayrollElement element)
        {
            element.UpdatedAtUtc = DateTime.UtcNow;

            const string sql =
                "UPDATE PayrollElements SET " +
                "NameFr = @NameFr, NameAr = @NameAr, Description = @Description, ElementType = @ElementType, " +
                "CalculationMethod = @CalculationMethod, CalculationBase = @CalculationBase, DefaultAmount = @DefaultAmount, " +
                "DefaultRate = @DefaultRate, DefaultQuantity = @DefaultQuantity, DefaultUnitPrice = @DefaultUnitPrice, " +
                "Periodicity = @Periodicity, IsCnasApplicable = @IsCnasApplicable, IsIrgApplicable = @IsIrgApplicable, " +
                "CnasPercent = @CnasPercent, IrgPercent = @IrgPercent, " +
                "IsIncludedInGross = @IsIncludedInGross, ExemptionCeiling = @ExemptionCeiling, IsEditable = @IsEditable, " +
                "IsEnabled = @IsEnabled, IsSystem = @IsSystem, DisplayOrder = @DisplayOrder, " +
                "InternalCode = @InternalCode, IsPrintable = @IsPrintable, IncludedInLissage = @IncludedInLissage, " +
                "IsAutomatic = @IsAutomatic, UpdatedAtUtc = @UpdatedAtUtc, " +
                "IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, element, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE PayrollElements SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        public bool ExistsById(long id)
        {
            return Connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM PayrollElements WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction) > 0;
        }
    }
}
