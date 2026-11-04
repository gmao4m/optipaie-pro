using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="Payslip"/>.</summary>
    internal sealed class PayslipRepository : RepositoryBase, IPayslipRepository
    {
        private const int Active = (int)PayslipStatus.Active;

        public PayslipRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public Payslip GetById(long id)
        {
            // By id: no status filter — the correction/deletion/restore flows need the exact row
            // whatever its lifecycle state.
            return Connection.QuerySingleOrDefault<Payslip>(
                "SELECT * FROM Payslips WHERE Id = @id;",
                new { id }, Transaction);
        }

        public IEnumerable<Payslip> GetByRun(long runId)
        {
            // ACTIVE only. Every live consumer (CNAS DAC/DAS, ATS history, the archive list and the
            // duplicate-payslip guard) reads through this, so a superseded or cancelled payslip is
            // automatically excluded everywhere without touching those call sites.
            return Connection.Query<Payslip>(
                "SELECT * FROM Payslips WHERE RunId = @runId AND Status = @Active ORDER BY Id;",
                new { runId, Active }, Transaction);
        }

        public IEnumerable<Payslip> GetByEmployee(long employeeId)
        {
            return Connection.Query<Payslip>(
                "SELECT * FROM Payslips WHERE EmployeeId = @employeeId AND Status = @Active " +
                "ORDER BY GeneratedAtUtc DESC, Id DESC;",
                new { employeeId, Active }, Transaction);
        }

        public IEnumerable<Payslip> GetInactiveByRun(long runId)
        {
            return Connection.Query<Payslip>(
                "SELECT * FROM Payslips WHERE RunId = @runId AND Status <> @Active ORDER BY Id;",
                new { runId, Active }, Transaction);
        }

        public bool HasAnyByEmployee(long employeeId)
        {
            return Connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM Payslips WHERE EmployeeId = @employeeId;",
                new { employeeId }, Transaction) > 0;
        }

        public long Insert(Payslip payslip)
        {
            payslip.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO Payslips " +
                "(RunId, EmployeeId, SalaireBrut, BaseCotisable, CnasEmployee, CnasEmployer, BaseImposable, " +
                " IrgBrut, Abattement, Irg, NetSalaire, CnasEmployeeRateUsed, CnasEmployerRateUsed, " +
                " WorkedDays, WorkedHours, EngineVersion, GeneratedAtUtc, CreatedAtUtc, " +
                " Status, SupersedesPayslipId, RevisionReason, RevisedByUser, RevisedAtUtc) " +
                "VALUES " +
                "(@RunId, @EmployeeId, @SalaireBrut, @BaseCotisable, @CnasEmployee, @CnasEmployer, @BaseImposable, " +
                " @IrgBrut, @Abattement, @Irg, @NetSalaire, @CnasEmployeeRateUsed, @CnasEmployerRateUsed, " +
                " @WorkedDays, @WorkedHours, @EngineVersion, @GeneratedAtUtc, @CreatedAtUtc, " +
                " @Status, @SupersedesPayslipId, @RevisionReason, @RevisedByUser, @RevisedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, ToRow(payslip), Transaction);
            payslip.Id = id;
            return id;
        }

        public void UpdateStatus(Payslip payslip)
        {
            const string sql =
                "UPDATE Payslips SET " +
                "Status = @Status, SupersedesPayslipId = @SupersedesPayslipId, " +
                "RevisionReason = @RevisionReason, RevisedByUser = @RevisedByUser, RevisedAtUtc = @RevisedAtUtc " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, new
            {
                payslip.Id,
                Status = (int)payslip.Status,
                payslip.SupersedesPayslipId,
                payslip.RevisionReason,
                payslip.RevisedByUser,
                payslip.RevisedAtUtc
            }, Transaction);
        }

        public void DeleteByRun(long runId)
        {
            Connection.Execute("DELETE FROM Payslips WHERE RunId = @runId;", new { runId }, Transaction);
        }

        /// <summary>Projects the payslip to an anonymous row with the enum stored as its integer value.</summary>
        private static object ToRow(Payslip p)
        {
            return new
            {
                p.RunId,
                p.EmployeeId,
                p.SalaireBrut,
                p.BaseCotisable,
                p.CnasEmployee,
                p.CnasEmployer,
                p.BaseImposable,
                p.IrgBrut,
                p.Abattement,
                p.Irg,
                p.NetSalaire,
                p.CnasEmployeeRateUsed,
                p.CnasEmployerRateUsed,
                p.WorkedDays,
                p.WorkedHours,
                p.EngineVersion,
                p.GeneratedAtUtc,
                p.CreatedAtUtc,
                Status = (int)p.Status,
                p.SupersedesPayslipId,
                p.RevisionReason,
                p.RevisedByUser,
                p.RevisedAtUtc
            };
        }
    }
}
