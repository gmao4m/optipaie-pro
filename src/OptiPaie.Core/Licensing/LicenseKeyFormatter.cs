using System.Text;

namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Formatting and validation for the enterprise license-key format
    /// <c>XXXXX-XXXXX-XXXXX-XXXXX</c> (four groups of five uppercase alphanumerics).
    /// Used to auto-format the activation textbox and validate before sending.
    /// </summary>
    public static class LicenseKeyFormatter
    {
        public const int GroupSize = 5;
        public const int GroupCount = 4;

        /// <summary>Total alphanumeric characters (excluding dashes).</summary>
        public const int RawLength = GroupSize * GroupCount;

        /// <summary>Keeps only A-Z / 0-9, upper-cased, capped at <see cref="RawLength"/>.</summary>
        public static string Clean(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(RawLength);
            foreach (char c in input)
            {
                char u = char.ToUpperInvariant(c);
                if ((u >= 'A' && u <= 'Z') || (u >= '0' && u <= '9'))
                {
                    sb.Append(u);
                    if (sb.Length == RawLength)
                    {
                        break;
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>Formats free input into <c>XXXXX-XXXXX-XXXXX-XXXXX</c> as far as it goes.</summary>
        public static string Format(string input)
        {
            string raw = Clean(input);
            var sb = new StringBuilder(RawLength + GroupCount - 1);
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && i % GroupSize == 0)
                {
                    sb.Append('-');
                }

                sb.Append(raw[i]);
            }

            return sb.ToString();
        }

        /// <summary>The canonical key (formatted, complete) or empty if not complete.</summary>
        public static string Canonical(string input)
        {
            string raw = Clean(input);
            return raw.Length == RawLength ? Format(raw) : string.Empty;
        }

        /// <summary>True when the input contains a complete, well-formed key.</summary>
        public static bool IsComplete(string input)
        {
            return Clean(input).Length == RawLength;
        }
    }
}
