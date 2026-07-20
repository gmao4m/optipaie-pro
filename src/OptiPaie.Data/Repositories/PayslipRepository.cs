using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="Payslip"/>.</summary>
    internal sealed class PayslipRepository : RepositoryBase, IPayslipRepository
    {
        public PayslipRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public Payslip GetById(long id)
        {
            return Connection.QuerySingleOrDefault<Payslip>(
                "SELECT * FROM Payslips WHERE Id = @id;",
                new { id }, Transaction);
        }

        public IEnumerable<Payslip> GetByRun(long runId)
        {
            return Connection.Query<Payslip>(
                "SELECT * FROM Payslips WHERE RunId = @runId ORDER BY Id;",
                new { runId }, Transaction);
        }

        public IEnumerable<Payslip> GetByEmployee(long employeeId)
        {
            return Connection.Query<Payslip>(
                "SELECT * FROM Payslips WHERE EmployeeId = @employeeId ORDER BY GeneratedAtUtc DESC, Id DESC;",
                new { employeeId }, Transaction);
        }

        public long Insert(Payslip payslip)
        {
            payslip.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO Payslips " +
                "(RunId, EmployeeId, SalaireBrut, BaseCotisable, CnasEmployee, CnasEmployer, BaseImposable, " +
                " IrgBrut, Abattement, Irg, NetSalaire, CnasEmployeeRateUsed, CnasEmployerRateUsed, " +
                " WorkedDays, WorkedHours, EngineVersion, GeneratedAtUtc, CreatedAtUtc) " +
                "VALUES " +
                "(@RunId, @EmployeeId, @SalaireBrut, @BaseCotisable, @CnasEmployee, @CnasEmployer, @BaseImposable, " +
                " @IrgBrut, @Abattement, @Irg, @NetSalaire, @CnasEmployeeRateUsed, @CnasEmployerRateUsed, " +
                " @WorkedDays, @WorkedHours, @EngineVersion, @GeneratedAtUtc, @CreatedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, payslip, Transaction);
            payslip.Id = id;
            return id;
        }

        public void DeleteByRun(long runId)
        {
            Connection.Execute("DELETE FROM Payslips WHERE RunId = @runId;", new { runId }, Transaction);
        }
    }
}
