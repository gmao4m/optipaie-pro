using System;

namespace OptiPaie.Data.Context
{
    /// <summary>
    /// Canonical binding of calendar days.
    /// <para>
    /// The connection stores dates as ISO-8601 with <see cref="DateTimeKind.Utc"/>.
    /// System.Data.SQLite renders the SAME day differently depending on the
    /// <see cref="DateTime.Kind"/> it is handed ("2025-06-01 00:00:00" for Utc,
    /// "2025-06-01 00:00:00Z" for Unspecified), so a value written from a date that
    /// came out of the database no longer matches one written from
    /// <c>new DateTime(...)</c> — and <c>WHERE Day = @day</c> silently returns nothing.
    /// </para>
    /// <para>
    /// Every repository that binds or stores a day goes through this helper, so a day
    /// has exactly one representation whichever module produced it.
    /// </para>
    /// </summary>
    internal static class SqliteDate
    {
        /// <summary>Midnight of the given day, always as UTC kind.</summary>
        public static DateTime Day(DateTime value)
        {
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
        }
    }
}
