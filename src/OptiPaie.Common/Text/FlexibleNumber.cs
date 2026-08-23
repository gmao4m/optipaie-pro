using System.Globalization;

namespace OptiPaie.Common.Text
{
    /// <summary>
    /// Culture-independent numeric input parsing for the whole app. The user may type either a
    /// comma OR a dot as the decimal separator (and may include grouping spaces / separators),
    /// regardless of the Windows regional configuration — "1250,50" and "1250.50" both mean 1250.5.
    ///
    /// Only the input/conversion is flexible; the payroll engine and every formula stay untouched.
    /// </summary>
    public static class FlexibleNumber
    {
        /// <summary>
        /// Parses <paramref name="raw"/> accepting comma or dot as the decimal separator, tolerating
        /// surrounding/grouping spaces and thousands separators. Returns false for null/blank/garbage.
        /// </summary>
        public static bool TryParse(string raw, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            // Strip every kind of whitespace (incl. non-breaking space used as a thousands group).
            string s = raw.Trim();
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            s = sb.ToString();
            if (s.Length == 0) return false;

            string normalized = NormalizeSeparators(s);
            return decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>Same as <see cref="TryParse(string, out decimal)"/> but yields a double.</summary>
        public static bool TryParse(string raw, out double value)
        {
            value = 0d;
            if (!TryParse(raw, out decimal dec)) return false;
            value = (double)dec;
            return true;
        }

        /// <summary>
        /// Collapses mixed comma/dot separators to a single invariant '.' decimal point:
        /// - both present  → the LAST-occurring one is the decimal point, the other is grouping (dropped);
        /// - one kind, appearing once → decimal point;
        /// - one kind, appearing several times → thousands grouping (all dropped).
        /// So "1250,50", "1250.50", "1,250.50", "1.250,50", "1.000.000" and "1,000,000" all parse correctly.
        /// </summary>
        private static string NormalizeSeparators(string s)
        {
            int lastComma = s.LastIndexOf(',');
            int lastDot = s.LastIndexOf('.');

            if (lastComma < 0 && lastDot < 0) return s; // pure integer

            int decimalPos;
            if (lastComma >= 0 && lastDot >= 0)
                decimalPos = lastComma > lastDot ? lastComma : lastDot; // the last separator wins
            else if (lastComma >= 0)
                decimalPos = s.IndexOf(',') == lastComma ? lastComma : -1; // single comma → decimal; many → grouping
            else
                decimalPos = s.IndexOf('.') == lastDot ? lastDot : -1;     // single dot → decimal; many → grouping

            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ',' || c == '.')
                {
                    if (i == decimalPos) sb.Append('.'); // the one true decimal point
                    // otherwise it's a grouping separator → drop it
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
