using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence for the per-company <see cref="CompanyWorkCalendar"/> (one row per company).</summary>
    public interface IWorkCalendarRepository
    {
        /// <summary>The stored calendar for a company, or null when none has been saved (⇒ defaults).</summary>
        CompanyWorkCalendar GetByCompany(long companyId);

        /// <summary>Inserts the company's calendar, or updates it if one already exists.</summary>
        void Upsert(CompanyWorkCalendar calendar);
    }
}
