using System.Text;

namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Formatting and validation for the license-key format issued by the backend
    /// (<c>gen_license_key</c>): a product prefix + three groups of four, e.g.
    /// <c>PAY-XXXX-XXXX-XXXX</c>. The client auto-formats the activation textbox and
    /// validates completeness before sending; the backend does the authoritative check.
    /// </summary>
    public static class LicenseKeyFormatter
    {
        /// <summary>Length of the product prefix (e.g. "PAY").</summary>
        public const int PrefixLength = 3;

        public const int GroupSize = 4;
        public const int GroupCount = 3;

        /// <summary>Total alphanumeric characters (prefix + groups, excluding dashes).</summary>
        public const int RawLength = PrefixLength + GroupSize * GroupCount;

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

        /// <summary>Formats free input into <c>PAY-XXXX-XXXX-XXXX</c> as far as it goes.</summary>
        public static string Format(string input)
        {
            string raw = Clean(input);
            var sb = new StringBuilder(RawLength + GroupCount);
            for (int i = 0; i < raw.Length; i++)
            {
                // Dash after the prefix, then after each group of four.
                if (i == PrefixLength || (i > PrefixLength && (i - PrefixLength) % GroupSize == 0))
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
