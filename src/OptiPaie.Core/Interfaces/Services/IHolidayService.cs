using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>Manage the public-holidays calendar, one year at a time.</summary>
    public interface IHolidayService
    {
        /// <summary>Stored holidays (national + company) for a year, ordered by date.</summary>
        IReadOnlyList<Holiday> GetForYear(long companyId, int year);

        Result<long> Add(Holiday holiday);

        Result Delete(long id);

        /// <summary>Pre-fills the fixed civil national holidays of a year (idempotent). Returns how many were added.</summary>
        int EnsureCivilForYear(long companyId, int year);
    }
}
