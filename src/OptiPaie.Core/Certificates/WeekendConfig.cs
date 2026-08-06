using System;
using System.Collections.Generic;
using System.Linq;

namespace OptiPaie.Core.Certificates
{
    /// <summary>
    /// Which days of the week are non-working. Default = the Algerian work week
    /// (weekend = Friday + Saturday). Ported verbatim from the source ATS/DRT tool.
    /// </summary>
    public class WeekendConfig
    {
        public HashSet<DayOfWeek> WeekendDays { get; set; }

        public WeekendConfig()
        {
            // Default: Algeria — Friday & Saturday are the weekend.
            WeekendDays = new HashSet<DayOfWeek> { DayOfWeek.Friday, DayOfWeek.Saturday };
        }

        public WeekendConfig(IEnumerable<DayOfWeek> weekendDays)
        {
            WeekendDays = new HashSet<DayOfWeek>(weekendDays);
        }

        public bool IsWeekend(DateTime date) => WeekendDays.Contains(date.DayOfWeek);

        /// <summary>Serializes to a compact string for storage, e.g. "Friday,Saturday".</summary>
        public string ToStorageString() => string.Join(",", WeekendDays.Select(d => d.ToString()));

        public static WeekendConfig FromStorageString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new WeekendConfig();

            var days = value.Split(',')
                             .Select(s => (DayOfWeek)Enum.Parse(typeof(DayOfWeek), s.Trim()))
                             .ToList();
            return new WeekendConfig(days);
        }
    }

    /// <summary>
    /// Business-day date arithmetic used to compute the dates on the official ATS/DRT
    /// certificates. Reverse-engineered from the original Excel/VBA tool and verified
    /// (4,000 date checks, 0 mismatches). Ported verbatim.
    /// </summary>
    public static class BusinessDayCalculator
    {
        /// <summary>
        /// The most recent business day strictly before <paramref name="date"/>.
        /// Used for "Date d'arrêt de travail" — the certificate shows the last day the
        /// employee actually worked, not the day the stoppage was recorded.
        /// </summary>
        public static DateTime PreviousBusinessDay(DateTime date, WeekendConfig config)
        {
            var d = date.AddDays(-1);
            while (config.IsWeekend(d))
                d = d.AddDays(-1);
            return d;
        }

        /// <summary>
        /// <paramref name="date"/> itself if it's a business day, otherwise the next
        /// business day. Used for "Date de reprise du travail" — an employee cannot
        /// resume on a weekend, so the next real working day is shown instead.
        /// </summary>
        public static DateTime NextBusinessDayOnOrAfter(DateTime date, WeekendConfig config)
        {
            var d = date;
            while (config.IsWeekend(d))
                d = d.AddDays(1);
            return d;
        }
    }
}
