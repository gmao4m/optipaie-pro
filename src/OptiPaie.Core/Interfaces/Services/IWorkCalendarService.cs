using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// The per-company work calendar: weekly pattern, half-day, rest-day rate and the proration
    /// basis, plus the "how many days was this person supposed to work" answer. Read-only w.r.t.
    /// the payroll engine; Commit 2 adds the data and the service — Commit 4 wires the basis into
    /// the payroll request. An untouched company keeps today's behaviour (defaults).
    /// </summary>
    public interface IWorkCalendarService
    {
        /// <summary>The company's calendar, or the defaults (today's behaviour) when none is saved.</summary>
        CompanyWorkCalendar GetSettings(long companyId);

        /// <summary>Saves the company's calendar (requires ManageAttendance). Future runs only.</summary>
        Result SaveSettings(CompanyWorkCalendar calendar);

        /// <summary>
        /// How many days the employee was supposed to work in the month: work-pattern days that fall
        /// inside the employment window (mid-month hire/departure) and are not holidays.
        /// </summary>
        int GetWorkableDays(long employeeId, int year, int month);

        /// <summary>The divisor a basis would use for the month (MonthDays→real days, FixedThirtieths→30, WorkingDays→workable days).</summary>
        decimal Denominator(ProrationBasis basis, int year, int month, int workableDays);

        /// <summary>The proration deduction = baseSalary × absentDays ÷ denominator, rounded to the centime.</summary>
        decimal Deduction(decimal baseSalary, decimal absentDays, decimal denominator);

        /// <summary>A live worked example (real employee + absent days) of the deduction under all three bases.</summary>
        ProrationExample BuildExample(long employeeId, int year, int month, int absentDays);
    }
}
