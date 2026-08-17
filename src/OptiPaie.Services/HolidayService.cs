using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>Manages the public-holidays calendar. Additive; never touches the payroll engine.</summary>
    public sealed class HolidayService : IHolidayService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public HolidayService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public IReadOnlyList<Holiday> GetForYear(long companyId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
                return uow.Holidays.GetForCompanyRange(companyId, new DateTime(year, 1, 1), new DateTime(year, 12, 31))
                    .OrderBy(h => h.HolidayDate).ToList();
        }

        public Result<long> Add(Holiday holiday)
        {
            if (holiday == null) return Result.Fail<long>("Jour férié manquant.", "Holiday_Required");
            if (string.IsNullOrWhiteSpace(holiday.NameAr)) return Result.Fail<long>("Le nom du jour férié est obligatoire.", "Holiday_NameRequired");

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
                return Result.Ok(uow.Holidays.Insert(holiday));
        }

        public Result Delete(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Holidays.SoftDelete(id);
                return Result.Ok();
            }
        }

        public int EnsureCivilForYear(long companyId, int year)
        {
            int added = 0;
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var existing = new HashSet<DateTime>(uow.Holidays
                    .GetForCompanyRange(companyId, new DateTime(year, 1, 1), new DateTime(year, 12, 31))
                    .Select(h => h.HolidayDate.Date));

                foreach (Holiday h in CivilHolidays(year))
                {
                    if (existing.Contains(h.HolidayDate.Date)) continue;
                    uow.Holidays.Insert(h);
                    added++;
                }
            }

            return added;
        }

        private static IEnumerable<Holiday> CivilHolidays(int year)
        {
            return new[]
            {
                new Holiday { CompanyId = null, IsReligious = false, HolidayDate = new DateTime(year, 1, 1), NameAr = "رأس السنة الميلادية" },
                new Holiday { CompanyId = null, IsReligious = false, HolidayDate = new DateTime(year, 1, 12), NameAr = "رأس السنة الأمازيغية (يناير)" },
                new Holiday { CompanyId = null, IsReligious = false, HolidayDate = new DateTime(year, 5, 1), NameAr = "عيد العمال" },
                new Holiday { CompanyId = null, IsReligious = false, HolidayDate = new DateTime(year, 7, 5), NameAr = "عيد الاستقلال" },
                new Holiday { CompanyId = null, IsReligious = false, HolidayDate = new DateTime(year, 11, 1), NameAr = "ذكرى ثورة أول نوفمبر" }
            };
        }
    }
}
