using System;
using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence for public holidays (national + per-company).</summary>
    public interface IHolidayRepository
    {
        Holiday GetById(long id);

        /// <summary>National + company holidays within an inclusive date range.</summary>
        IEnumerable<Holiday> GetForCompanyRange(long companyId, DateTime from, DateTime to);

        long Insert(Holiday holiday);

        void SoftDelete(long id);
    }
}
