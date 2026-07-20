using System;
using System.Globalization;

namespace OptiPaie.Core.Primitives
{
    /// <summary>
    /// An immutable payroll month, identified by year and month (1-12).
    /// <para>
    /// Payroll in Algeria is computed per calendar month; this value object is the
    /// canonical period key used for run uniqueness, archive search and the
    /// month-by-month lissage logic. It carries only calendar arithmetic — no
    /// payroll business rules.
    /// </para>
    /// </summary>
    public readonly struct PayrollPeriod : IEquatable<PayrollPeriod>, IComparable<PayrollPeriod>
    {
        /// <summary>Four-digit year (2000-2100, a defensive sanity range).</summary>
        public int Year { get; }

        /// <summary>Month number, 1 (January) to 12 (December).</summary>
        public int Month { get; }

        /// <summary>Creates a payroll period, validating the year and month.</summary>
        public PayrollPeriod(int year, int month)
        {
            if (year < 2000 || year > 2100)
            {
                throw new ArgumentOutOfRangeException(nameof(year), year,
                    "Year must be between 2000 and 2100.");
            }

            if (month < 1 || month > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(month), month,
                    "Month must be between 1 and 12.");
            }

            Year = year;
            Month = month;
        }

        /// <summary>The first calendar day of the period.</summary>
        public DateTime FirstDay => new DateTime(Year, Month, 1);

        /// <summary>The last calendar day of the period.</summary>
        public DateTime LastDay => new DateTime(Year, Month, DateTime.DaysInMonth(Year, Month));

        /// <summary>Number of calendar days in the period's month.</summary>
        public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

        /// <summary>A sortable integer key (year * 100 + month), e.g. 202606.</summary>
        public int ToSortableKey() => (Year * 100) + Month;

        /// <summary>Returns the period offset by the given number of months (may be negative).</summary>
        public PayrollPeriod AddMonths(int months)
        {
            DateTime shifted = FirstDay.AddMonths(months);
            return new PayrollPeriod(shifted.Year, shifted.Month);
        }

        /// <summary>The payroll period containing the given date.</summary>
        public static PayrollPeriod FromDate(DateTime date) => new PayrollPeriod(date.Year, date.Month);

        public bool Equals(PayrollPeriod other) => Year == other.Year && Month == other.Month;

        public override bool Equals(object obj) => obj is PayrollPeriod other && Equals(other);

        public override int GetHashCode() => ToSortableKey();

        public int CompareTo(PayrollPeriod other) => ToSortableKey().CompareTo(other.ToSortableKey());

        public static bool operator ==(PayrollPeriod left, PayrollPeriod right) => left.Equals(right);

        public static bool operator !=(PayrollPeriod left, PayrollPeriod right) => !left.Equals(right);

        public static bool operator >(PayrollPeriod left, PayrollPeriod right) => left.CompareTo(right) > 0;

        public static bool operator <(PayrollPeriod left, PayrollPeriod right) => left.CompareTo(right) < 0;

        public static bool operator >=(PayrollPeriod left, PayrollPeriod right) => left.CompareTo(right) >= 0;

        public static bool operator <=(PayrollPeriod left, PayrollPeriod right) => left.CompareTo(right) <= 0;

        /// <summary>Culture-invariant "MM/yyyy" representation.</summary>
        public override string ToString() =>
            Month.ToString("00", CultureInfo.InvariantCulture) + "/" + Year.ToString(CultureInfo.InvariantCulture);
    }
}
