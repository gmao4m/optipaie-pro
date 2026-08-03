using System;
using System.Linq;

namespace OptiPaie.Services.Cnas
{
    /// <summary>
    /// THE single source of truth for CNAS identity validation. Both the preparation screen
    /// (CheckReadiness, tranche 1) and the DAS export controller (étape 8) consume these exact
    /// rules — so a blocker is never discovered at export while preparation showed all-green.
    /// Pure predicates over raw values; no I/O, no state. The exact CNAS formats are working
    /// assumptions (à confirmer) kept here so they change in ONE place.
    /// </summary>
    public static class CnasIdentityRules
    {
        public const int NssLength = 12;       // NSS = 12 chiffres (clé incluse) — à confirmer
        public const int EmployerDigits = 10;  // n° employeur = 10 chiffres — à confirmer

        public static bool IsNssMissing(string nss) => Trim(nss).Length == 0;

        public static bool IsNssMalformed(string nss)
        {
            string n = Trim(nss);
            return n.Length != 0 && !(n.Length == NssLength && n.All(char.IsDigit));
        }

        public static bool IsBirthDateMissing(DateTime? birthDate) => birthDate == null;

        public static bool IsEmployerNumberMissing(string employer) => Trim(employer).Length == 0;

        public static bool IsEmployerNumberMalformed(string employer)
        {
            string e = Trim(employer);
            return e.Length != 0 && DigitsOnly(e).Length != EmployerDigits;
        }

        private static string Trim(string s) => (s ?? string.Empty).Trim();
        private static string DigitsOnly(string s) => new string(s.Where(char.IsDigit).ToArray());
    }
}
