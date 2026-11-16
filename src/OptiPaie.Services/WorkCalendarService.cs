using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;

namespace OptiPaie.Services
{
    /// <summary>
    /// The per-company work calendar + the workable-days / proration maths. Additive; never touches
    /// the payroll engine. The default basis (<see cref="ProrationBasis.MonthDays"/>) reproduces the
    /// product's historical proration exactly, so an untouched company is unchanged.
    /// </summary>
    public sealed class WorkCalendarService : IWorkCalendarService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IHolidayService _holidays;
        private readonly IAccessContext _access;

        public WorkCalendarService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IHolidayService holidays,
            IAccessContext access = null)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _holidays = Guard.AgainstNull(holidays, nameof(holidays));
            _access = access;
        }

        public CompanyWorkCalendar GetSettings(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.WorkCalendar.GetByCompany(companyId)
                    ?? new CompanyWorkCalendar { CompanyId = companyId }; // defaults = today's behaviour
            }
        }

        public Result SaveSettings(CompanyWorkCalendar calendar)
        {
            AccessPolicy.Require(_access, Permission.ManageAttendance);
            if (calendar == null || calendar.CompanyId <= 0)
            {
                return Result.Fail("Entreprise manquante.", "WorkCalendar_CompanyRequired");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.WorkCalendar.Upsert(calendar);
                return Result.Ok();
            }
        }

        public int GetWorkableDays(long employeeId, int year, int month)
        {
            if (!ValidPeriod(year, month)) return 0;

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee e = uow.Employees.GetById(employeeId);
                if (e == null || e.IsDeleted) return 0;

                CompanyWorkCalendar settings = uow.WorkCalendar.GetByCompany(e.CompanyId)
                    ?? new CompanyWorkCalendar { CompanyId = e.CompanyId };

                var monthStart = new DateTime(year, month, 1);
                var monthEnd = new DateTime(year, month, DateTime.DaysInMonth(year, month));

                DateTime start = e.HireDate.Date > monthStart ? e.HireDate.Date : monthStart;
                DateTime end = e.ExitDate.HasValue && e.ExitDate.Value.Date < monthEnd ? e.ExitDate.Value.Date : monthEnd;

                HashSet<DateTime> holidays = HolidaysForMonth(e.CompanyId, year, month);

                int count = 0;
                for (DateTime d = start; d <= end; d = d.AddDays(1))
                {
                    if (settings.IsWorkDay(d.DayOfWeek) && !holidays.Contains(d.Date))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public decimal Denominator(ProrationBasis basis, int year, int month, int workableDays)
        {
            switch (basis)
            {
                case ProrationBasis.FixedThirtieths:
                    return 30m;
                case ProrationBasis.WorkingDays:
                    return workableDays;
                default: // MonthDays — today's behaviour
                    return ValidPeriod(year, month) ? DateTime.DaysInMonth(year, month) : 0m;
            }
        }

        public decimal Deduction(decimal baseSalary, decimal absentDays, decimal denominator)
        {
            if (denominator <= 0m || baseSalary <= 0m || absentDays <= 0m) return 0m;
            return Math.Round(baseSalary * absentDays / denominator, 2, MidpointRounding.AwayFromZero);
        }

        public ProrationExample BuildExample(long employeeId, int year, int month, decimal absentDays)
        {
            AccessPolicy.Require(_access, Permission.ViewSalary); // the example reveals the salary
            if (absentDays < 0m) absentDays = 0m;

            Employee e;
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                e = uow.Employees.GetById(employeeId);
            }
            if (e == null) return null;

            int monthDays = ValidPeriod(year, month) ? DateTime.DaysInMonth(year, month) : 0;
            int workableDays = GetWorkableDays(employeeId, year, month);

            var rows = new List<ProrationExampleRow>();
            foreach (ProrationBasis basis in new[] { ProrationBasis.MonthDays, ProrationBasis.FixedThirtieths, ProrationBasis.WorkingDays })
            {
                decimal denom = Denominator(basis, year, month, workableDays);
                rows.Add(new ProrationExampleRow
                {
                    Basis = basis,
                    Denominator = denom,
                    Deduction = Deduction(e.BaseSalary, absentDays, denom),
                    IsCurrentDefault = basis == ProrationBasis.MonthDays
                });
            }

            return new ProrationExample
            {
                EmployeeName = (e.FirstNameFr + " " + e.LastNameFr).Trim(),
                BaseSalary = e.BaseSalary,
                AbsentDays = absentDays,
                Year = year,
                Month = month,
                MonthDays = monthDays,
                WorkableDays = workableDays,
                Rows = rows
            };
        }

        public ProrationResult ResolveForPayroll(long companyId, long employeeId, int year, int month,
            AttendanceSummary summary, bool attendanceEnabled)
        {
            ProrationBasis basis = GetSettings(companyId).ProrationBasis;

            // Divisor per basis. GetWorkableDays is only needed for the WorkingDays basis (a per-employee
            // query), so the default path (MonthDays) stays a pure calendar calculation — exactly today.
            int workableCalendar = basis == ProrationBasis.WorkingDays ? GetWorkableDays(employeeId, year, month) : 0;
            decimal divisor = Denominator(basis, year, month, workableCalendar);

            bool hasAttendance = attendanceEnabled && summary != null && summary.RecordedDays > 0m;
            decimal deduct = hasAttendance ? summary.DeductibleAbsenceDays : 0m;
            decimal workedDays = Math.Max(0m, divisor - deduct);

            return new ProrationResult
            {
                WorkedDays = workedDays,
                WorkableDays = divisor,
                Basis = basis,
                Divisor = divisor,
                DeductibleDays = deduct,
                HasAttendance = hasAttendance
            };
        }

        private HashSet<DateTime> HolidaysForMonth(long companyId, int year, int month)
        {
            var set = new HashSet<DateTime>();
            // Stored holidays (national + company) for the year, kept to the month.
            foreach (Holiday h in _holidays.GetForYear(companyId, year))
            {
                if (h.HolidayDate.Year == year && h.HolidayDate.Month == month)
                {
                    set.Add(h.HolidayDate.Date);
                }
            }
            // Fixed civil national holidays — a safety net so workable days exclude them even when the
            // year was never pre-filled (same fixed dates the Holidays feature knows: 1 & 12 Jan, 1 May,
            // 5 Jul, 1 Nov). Religious holidays move each year and must be entered, so they are not here.
            foreach ((int m, int d) in new[] { (1, 1), (1, 12), (5, 1), (7, 5), (11, 1) })
            {
                if (m == month) set.Add(new DateTime(year, m, d));
            }
            return set;
        }

        private static bool ValidPeriod(int year, int month) =>
            year >= 2000 && year <= 2100 && month >= 1 && month <= 12;
    }
}
