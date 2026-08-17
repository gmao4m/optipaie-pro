using System;
using System.Collections.Generic;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// An employee's annual-leave position for a year, derived from the shared
    /// employee record (hire date) and the approved requests. Never stored — always
    /// computed, so it cannot drift out of date.
    /// </summary>
    public sealed class LeaveBalance
    {
        public long EmployeeId { get; set; }

        /// <summary>Display name (filled when listing a whole company).</summary>
        public string EmployeeName { get; set; }

        public int Year { get; set; }

        /// <summary>Days earned: 2,5 per month worked in the year, capped at 30.</summary>
        public decimal Entitlement { get; set; }

        /// <summary>Approved annual-leave days for the year.</summary>
        public decimal Taken { get; set; }

        /// <summary>Approved annual-leave days still awaiting a decision.</summary>
        public decimal Pending { get; set; }

        /// <summary>Entitlement − taken (pending is shown separately, not deducted).</summary>
        public decimal Remaining { get; set; }

        /// <summary>
        /// Solde disponible = acquis − consommé − en attente. Unlike <see cref="Remaining"/>,
        /// this DOES reserve the pending (submitted-but-undecided) days, so two requests can
        /// never engage the same days twice. This is the value the service validates against.
        /// </summary>
        public decimal Available { get; set; }

        /// <summary>Approved days of every other type (sick, unpaid, maternity, special).</summary>
        public decimal OtherLeaveDays { get; set; }

        /// <summary>Approved UNPAID days — the ones payroll deducts.</summary>
        public decimal UnpaidDays { get; set; }
    }

    /// <summary>Module settings driving the leave calculations.</summary>
    public sealed class LeaveSettings
    {
        /// <summary>Days earned per month worked (loi 90-11 art. 41: 2,5).</summary>
        public decimal DaysPerMonth { get; set; } = 2.5m;

        /// <summary>Yearly cap on annual leave (30 days).</summary>
        public decimal AnnualCap { get; set; } = 30m;

        /// <summary>Whether the company weekly rest days are excluded from the count.</summary>
        public bool ExcludeRestDays { get; set; } = true;

        /// <summary>
        /// The company's weekly rest days, excluded from the day count when
        /// <see cref="ExcludeRestDays"/> is on. Default = the Algerian weekend (Friday + Saturday);
        /// configurable per company.
        /// </summary>
        public ISet<DayOfWeek> WeekendDays { get; set; } =
            new HashSet<DayOfWeek> { DayOfWeek.Friday, DayOfWeek.Saturday };

        // --- Regulatory options. Each DEFAULTS to the historical behaviour so an existing
        //     database behaves EXACTLY as before; a company opts in to the legal rule. ---

        /// <summary>Count public holidays out of a leave period. Default OFF (holidays counted, as before).</summary>
        public bool ExcludeHolidays { get; set; }

        /// <summary>Count in calendar days (weekend included) instead of working days. Default OFF (working days).</summary>
        public bool CalendarDayCount { get; set; }

        /// <summary>Accrue on the 1 July → 30 June reference period instead of the civil year. Default OFF (civil year).</summary>
        public bool ReferenceJulyToJune { get; set; }

        /// <summary>Unpaid-leave months do not accrue annual entitlement. Default OFF (accrue as before).</summary>
        public bool AccrualExcludesUnpaid { get; set; }

        /// <summary>Apply the strict legal treatment to CNAS-paid leave (employer suspends the salary). Default OFF (salary maintained, as today).</summary>
        public bool StrictCnasTreatment { get; set; }

        /// <summary>Maternity duration in days (loi 25-08/2025 = 150 ; à vérifier au Journal Officiel). Informative parameter.</summary>
        public decimal MaternityDays { get; set; } = 150m;
    }

    /// <summary>Live preview of a leave request BEFORE it is saved (days, payment, balance impact, blocking reason).</summary>
    public sealed class LeavePreview
    {
        public decimal Days { get; set; }
        public PaymentCategory Category { get; set; }
        public bool DecrementsBalance { get; set; }
        public decimal AvailableBefore { get; set; }
        public decimal AvailableAfter { get; set; }
        public bool Ok { get; set; }
        public string Reason { get; set; }
        public string ReasonCode { get; set; }
    }

    /// <summary>One month of the year in the annual-leave accrual detail.</summary>
    public sealed class AccrualMonth
    {
        public int Month { get; set; }
        public bool Present { get; set; }
        public decimal UnpaidDays { get; set; }
        public decimal Accrued { get; set; }
    }

    /// <summary>Reliquat de congé (solde de tout compte) for an employee leaving the company.</summary>
    public sealed class FinalSettlement
    {
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public DateTime ExitDate { get; set; }
        public decimal Acquired { get; set; }
        public decimal Taken { get; set; }
        public decimal RemainingDays { get; set; }
        public decimal MonthlySalary { get; set; }
        public decimal DailyRate { get; set; }
        public decimal Amount { get; set; }
    }
}
