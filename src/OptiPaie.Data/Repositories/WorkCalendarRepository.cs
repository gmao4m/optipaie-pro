using System;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper persistence for <see cref="CompanyWorkCalendar"/> (one row per company).</summary>
    internal sealed class WorkCalendarRepository : RepositoryBase, IWorkCalendarRepository
    {
        public WorkCalendarRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public CompanyWorkCalendar GetByCompany(long companyId)
        {
            return Connection.QuerySingleOrDefault<CompanyWorkCalendar>(
                "SELECT * FROM CompanyWorkCalendar WHERE CompanyId = @companyId;",
                new { companyId }, Transaction);
        }

        public void Upsert(CompanyWorkCalendar c)
        {
            if (c.CreatedAtUtc == null) c.CreatedAtUtc = DateTime.UtcNow;
            c.UpdatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO CompanyWorkCalendar " +
                "(CompanyId, WorkMon, WorkTue, WorkWed, WorkThu, WorkFri, WorkSat, WorkSun, " +
                " HalfDayHours, RestDayRate, ProrationBasis, CreatedAtUtc, UpdatedAtUtc) " +
                "VALUES " +
                "(@CompanyId, @WorkMon, @WorkTue, @WorkWed, @WorkThu, @WorkFri, @WorkSat, @WorkSun, " +
                " @HalfDayHours, @RestDayRate, @ProrationBasis, @CreatedAtUtc, @UpdatedAtUtc) " +
                "ON CONFLICT(CompanyId) DO UPDATE SET " +
                "  WorkMon = excluded.WorkMon, WorkTue = excluded.WorkTue, WorkWed = excluded.WorkWed, " +
                "  WorkThu = excluded.WorkThu, WorkFri = excluded.WorkFri, WorkSat = excluded.WorkSat, " +
                "  WorkSun = excluded.WorkSun, HalfDayHours = excluded.HalfDayHours, " +
                "  RestDayRate = excluded.RestDayRate, ProrationBasis = excluded.ProrationBasis, " +
                "  UpdatedAtUtc = excluded.UpdatedAtUtc;";

            Connection.Execute(sql, new
            {
                c.CompanyId,
                WorkMon = c.WorkMon ? 1 : 0,
                WorkTue = c.WorkTue ? 1 : 0,
                WorkWed = c.WorkWed ? 1 : 0,
                WorkThu = c.WorkThu ? 1 : 0,
                WorkFri = c.WorkFri ? 1 : 0,
                WorkSat = c.WorkSat ? 1 : 0,
                WorkSun = c.WorkSun ? 1 : 0,
                c.HalfDayHours,
                c.RestDayRate,
                ProrationBasis = (int)c.ProrationBasis,
                c.CreatedAtUtc,
                c.UpdatedAtUtc
            }, Transaction);
        }
    }
}
