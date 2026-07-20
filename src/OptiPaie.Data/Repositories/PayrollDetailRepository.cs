using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="PayrollDetail"/> lines.</summary>
    internal sealed class PayrollDetailRepository : RepositoryBase, IPayrollDetailRepository
    {
        public PayrollDetailRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public IEnumerable<PayrollDetail> GetByPayslip(long payslipId)
        {
            return Connection.Query<PayrollDetail>(
                "SELECT * FROM PayrollDetails WHERE PayslipId = @payslipId ORDER BY DisplayOrder, Id;",
                new { payslipId }, Transaction);
        }

        public long Insert(PayrollDetail detail)
        {
            detail.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO PayrollDetails " +
                "(PayslipId, ElementId, LabelFr, LabelAr, ElementType, Base, Rate, Quantity, UnitPrice, " +
                " Amount, IsCnasApplicable, IsIrgApplicable, DisplayOrder, CreatedAtUtc) " +
                "VALUES " +
                "(@PayslipId, @ElementId, @LabelFr, @LabelAr, @ElementType, @Base, @Rate, @Quantity, @UnitPrice, " +
                " @Amount, @IsCnasApplicable, @IsIrgApplicable, @DisplayOrder, @CreatedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, detail, Transaction);
            detail.Id = id;
            return id;
        }

        public void DeleteByPayslip(long payslipId)
        {
            Connection.Execute("DELETE FROM PayrollDetails WHERE PayslipId = @payslipId;", new { payslipId }, Transaction);
        }
    }
}
