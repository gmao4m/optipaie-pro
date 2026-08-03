using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OptiPaie.Services.Cnas
{
    /// <summary>Raised when a value cannot be encoded into the DAS format (non-ASCII, or too wide).</summary>
    public sealed class DasEncodingException : Exception
    {
        public DasEncodingException(string message) : base(message) { }
    }

    /// <summary>
    /// Builds one fixed-width DAS line. Starts as a full row of spaces, then each value is placed
    /// into its field per the spec (offset/length/alignment). Values are validated: any non-ASCII
    /// character or a value wider than its field is refused (a DAS amount is never truncated). The
    /// layout comes entirely from <see cref="DasFieldSpec"/> — this class holds only placement logic.
    /// </summary>
    public sealed class DasLineBuilder
    {
        private readonly char[] _buffer;

        public DasLineBuilder(int length)
        {
            _buffer = new char[length];
            for (int i = 0; i < length; i++)
            {
                _buffer[i] = ' ';
            }
        }

        public DasLineBuilder Put(DasFieldSpec field, string value)
        {
            value = value ?? string.Empty;

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] > (char)127)
                {
                    throw new DasEncodingException(
                        "Champ « " + field.Name + " » : caractère non-ASCII « " + value[i] + " ».");
                }
            }

            if (value.Length > field.Length)
            {
                throw new DasEncodingException(
                    "Champ « " + field.Name + " » : « " + value + " » dépasse " + field.Length +
                    " caractères — l'export est refusé, jamais tronqué.");
            }

            int start = field.Align == DasAlign.Right
                ? field.Offset + field.Length - value.Length   // right-aligned, space-filled on the left
                : field.Offset;                                // left-aligned, space-filled on the right
            for (int i = 0; i < value.Length; i++)
            {
                _buffer[start + i] = value[i];
            }

            return this;
        }

        public string Build() => new string(_buffer);
    }

    /// <summary>Turns domain values into the exact string shapes the DAS fields expect.</summary>
    public static class DasFormat
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>A DA amount as integer centimes, no sign, no separator (84000,00 DA → "8400000").</summary>
        public static string Amount(decimal da)
        {
            decimal centimes = decimal.Round(da * 100m, 0, MidpointRounding.AwayFromZero);
            return centimes.ToString("0", Inv);
        }

        /// <summary>A date as jjmmaaaa, or empty (the field then stays spaces) when absent.</summary>
        public static string Date(DateTime? d) => d.HasValue ? d.Value.ToString("ddMMyyyy", Inv) : string.Empty;

        /// <summary>A whole number as plain digits (worker count, duration, line number).</summary>
        public static string Int(int value) => value.ToString("0", Inv);
    }

    /// <summary>
    /// Serializes DAS lines to bytes: pure ASCII, NO byte-order mark. The header file is one line
    /// followed by a trailing CR LF; the detail file is its lines joined by CR LF with NO trailing
    /// CR LF. Any non-ASCII character is refused rather than silently substituted.
    /// </summary>
    public static class DasAsciiWriter
    {
        public static byte[] WriteHeader(string headerLine) => ToAscii(headerLine + "\r\n");

        public static byte[] WriteDetail(IReadOnlyList<string> detailLines) =>
            ToAscii(string.Join("\r\n", detailLines));

        private static byte[] ToAscii(string content)
        {
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] > (char)127)
                {
                    throw new DasEncodingException("Caractère non-ASCII dans le fichier DAS : « " + content[i] + " ».");
                }
            }

            // ASCIIEncoding never emits a BOM.
            return new ASCIIEncoding().GetBytes(content);
        }
    }
}
