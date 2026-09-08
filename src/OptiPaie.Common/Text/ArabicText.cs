using System.Text;

namespace OptiPaie.Common.Text
{
    /// <summary>
    /// Helpers for laying out Arabic text in the QuestPDF documents (fiche, attestations…).
    ///
    /// QuestPDF 2022.12 shapes Arabic correctly (letters join) and orders a pure-Arabic run
    /// right-to-left, BUT it has no real Unicode bidi: inside a right-to-left (Arabic) paragraph
    /// it REVERSES every run of Western digits/separators — so "رقم 24" prints "رقم 42" and a date
    /// "01/03/2018" prints "8102/30/10". <see cref="FixRtlDigits"/> pre-reverses those runs so the
    /// engine's reversal restores the correct reading. It only touches strings that actually contain
    /// Arabic letters, so passing a pure-Latin or pure-numeric string through it is a no-op.
    ///
    /// The other rule (enforced by the callers, not here): never place a multi-word Arabic run and
    /// Latin text in the SAME text element — that mangles the Arabic. Each value gets its own element.
    /// </summary>
    public static class ArabicText
    {
        /// <summary>True if the string contains at least one Arabic-script character.</summary>
        public static bool ContainsArabic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (IsArabic(c)) return true;
            return false;
        }

        /// <summary>
        /// If <paramref name="s"/> contains Arabic letters, reverses each maximal run of Western
        /// digits and their separators (0-9 . , / - :) so that after QuestPDF's naive right-to-left
        /// reversal the number/date reads correctly. No-op for strings with no Arabic.
        /// </summary>
        public static string FixRtlDigits(string s)
        {
            if (!ContainsArabic(s)) return s;

            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                if (IsNumeric(s[i]))
                {
                    int j = i;
                    while (j < s.Length && IsNumeric(s[j])) j++;
                    for (int k = j - 1; k >= i; k--) sb.Append(s[k]); // reverse the numeric run
                    i = j;
                }
                else
                {
                    sb.Append(s[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static bool IsArabic(char c) =>
            (c >= '؀' && c <= 'ۿ') || (c >= 'ݐ' && c <= 'ݿ') ||
            (c >= 'ࢠ' && c <= 'ࣿ') || (c >= 'ﭐ' && c <= '﷿') ||
            (c >= 'ﹰ' && c <= '﻿');

        private static bool IsNumeric(char c) =>
            (c >= '0' && c <= '9') || c == '.' || c == ',' || c == '/' || c == '-' || c == ':';
    }
}
