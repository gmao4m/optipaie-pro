using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper persistence for <see cref="Holiday"/> (national + per-company).</summary>
    internal sealed class HolidayRepository : RepositoryBase, IHolidayRepository
    {
        public HolidayRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public Holiday GetById(long id)
        {
            return Connection.QuerySingleOrDefault<Holiday>(
                "SELECT * FROM Holidays WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public IEnumerable<Holiday> GetForCompanyRange(long companyId, DateTime from, DateTime to)
        {
            return Connection.Query<Holiday>(
                "SELECT * FROM Holidays WHERE IsDeleted = 0 " +
                "  AND (CompanyId IS NULL OR CompanyId = @companyId) " +
                "  AND HolidayDate >= @from AND HolidayDate <= @to " +
                "ORDER BY HolidayDate;",
                new { companyId, from = SqliteDate.Day(from), to = SqliteDate.Day(to) }, Transaction);
        }

        public long Insert(Holiday holiday)
        {
            holiday.CreatedAtUtc = DateTime.UtcNow;
            holiday.HolidayDate = SqliteDate.Day(holiday.HolidayDate);

            const string sql =
                "INSERT INTO Holidays (CompanyId, HolidayDate, NameAr, IsReligious, CreatedAtUtc, IsDeleted) " +
                "VALUES (@CompanyId, @HolidayDate, @NameAr, @IsReligious, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, holiday, Transaction);
            holiday.Id = id;
            return id;
        }

        public void SoftDelete(long id)
        {
            Connection.Execute("UPDATE Holidays SET IsDeleted = 1 WHERE Id = @id;", new { id }, Transaction);
        }
    }
}
