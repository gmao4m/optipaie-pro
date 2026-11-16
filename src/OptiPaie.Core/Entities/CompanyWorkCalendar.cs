using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A company's work calendar: which weekdays are work days, the half-day definition, the
    /// rate for work performed on a rest day or holiday, and the proration basis. All defaults
    /// reproduce the product's current behaviour, so an untouched company is unchanged.
    /// Holidays are NOT here — they reuse the existing <see cref="Holiday"/> calendar.
    /// </summary>
    public sealed class CompanyWorkCalendar
    {
        public long CompanyId { get; set; }

        // Weekly pattern — Algerian default is Sun–Thu work, Fri–Sat rest.
        public bool WorkMon { get; set; } = true;
        public bool WorkTue { get; set; } = true;
        public bool WorkWed { get; set; } = true;
        public bool WorkThu { get; set; } = true;
        public bool WorkFri { get; set; } = false;
        public bool WorkSat { get; set; } = false;
        public bool WorkSun { get; set; } = true;

        /// <summary>Hours that count as a half day (default 4).</summary>
        public decimal HalfDayHours { get; set; } = 4m;

        /// <summary>Pay multiplier for work performed on a rest day or holiday (default 1 = no premium, today's behaviour).</summary>
        public decimal RestDayRate { get; set; } = 1m;

        /// <summary>How a partial month is prorated. Default <see cref="ProrationBasis.MonthDays"/> = today.</summary>
        public ProrationBasis ProrationBasis { get; set; } = ProrationBasis.MonthDays;

        public DateTime? CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }

        /// <summary>True when the given weekday is a work day under this pattern.</summary>
        public bool IsWorkDay(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return WorkMon;
                case DayOfWeek.Tuesday: return WorkTue;
                case DayOfWeek.Wednesday: return WorkWed;
                case DayOfWeek.Thursday: return WorkThu;
                case DayOfWeek.Friday: return WorkFri;
                case DayOfWeek.Saturday: return WorkSat;
                default: return WorkSun;
            }
        }
    }
}
