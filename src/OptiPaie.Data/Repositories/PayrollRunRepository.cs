using System;
using System.Collections.Generic;
using System.Text;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="PayrollRun"/>.</summary>
    internal sealed class PayrollRunRepository : RepositoryBase, IPayrollRunRepository
    {
        public PayrollRunRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public PayrollRun GetById(long id)
        {
            return Connection.QuerySingleOrDefault<PayrollRun>(
                "SELECT * FROM PayrollRuns WHERE Id = @id;",
                new { id }, Transaction);
        }

        public PayrollRun GetByCompanyAndPeriod(long companyId, int year, int month)
        {
            return Connection.QuerySingleOrDefault<PayrollRun>(
                "SELECT * FROM PayrollRuns WHERE CompanyId = @companyId AND PeriodYear = @year AND PeriodMonth = @month;",
                new { companyId, year, month }, Transaction);
        }

        public IEnumerable<PayrollRun> Search(long? companyId, int? year, int? month)
        {
            var sql = new StringBuilder("SELECT * FROM PayrollRuns WHERE 1 = 1");
            var parameters = new DynamicParameters();

            if (companyId.HasValue)
            {
                sql.Append(" AND CompanyId = @companyId");
                parameters.Add("@companyId", companyId.Value);
            }

            if (year.HasValue)
            {
                sql.Append(" AND PeriodYear = @year");
                parameters.Add("@year", year.Value);
            }

            if (month.HasValue)
            {
                sql.Append(" AND PeriodMonth = @month");
                parameters.Add("@month", month.Value);
            }

            sql.Append(" ORDER BY PeriodYear DESC, PeriodMonth DESC, Id DESC;");

            return Connection.Query<PayrollRun>(sql.ToString(), parameters, Transaction);
        }

        public long Insert(PayrollRun run)
        {
            run.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO PayrollRuns " +
                "(CompanyId, PeriodYear, PeriodMonth, RunStatus, GeneratedAtUtc, EngineVersion, CreatedAtUtc) " +
                "VALUES " +
                "(@CompanyId, @PeriodYear, @PeriodMonth, @RunStatus, @GeneratedAtUtc, @EngineVersion, @CreatedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, run, Transaction);
            run.Id = id;
            return id;
        }

        public void Update(PayrollRun run)
        {
            const string sql =
                "UPDATE PayrollRuns SET " +
                "CompanyId = @CompanyId, PeriodYear = @PeriodYear, PeriodMonth = @PeriodMonth, " +
                "RunStatus = @RunStatus, GeneratedAtUtc = @GeneratedAtUtc, EngineVersion = @EngineVersion " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, run, Transaction);
        }

        public bool ExistsForPeriod(long companyId, int year, int month)
        {
            return Connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM PayrollRuns WHERE CompanyId = @companyId AND PeriodYear = @year AND PeriodMonth = @month;",
                new { companyId, year, month }, Transaction) > 0;
        }
    }
}
