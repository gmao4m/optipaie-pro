using System.Globalization;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Converts a dinar amount to its French wording (for the "arrêté à la somme de" line).</summary>
    public static class FrenchAmount
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private static readonly string[] Small =
        {
            "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf",
            "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize"
        };

        public static string InWords(decimal net)
        {
            long dinars = (long)decimal.Truncate(net);
            int centimes = (int)decimal.Round((net - dinars) * 100m);
            if (dinars > 999999999L)
            {
                return string.Empty;
            }

            string words = Words(dinars);
            string text = char.ToUpper(words[0], Fr) + words.Substring(1) + " dinars algériens";
            if (centimes > 0)
            {
                text += " et " + centimes.ToString("00", Fr) + " centimes";
            }

            return text;
        }

        private static string Below100(int n)
        {
            if (n < 17) return Small[n];
            if (n < 20) return "dix-" + Small[n - 10];

            int t = n / 10;
            int u = n % 10;
            switch (t)
            {
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                    string b = new[] { "vingt", "trente", "quarante", "cinquante", "soixante" }[t - 2];
                    if (u == 0) return b;
                    if (u == 1) return b + " et un";
                    return b + "-" + Small[u];
                case 7:
                    if (u == 1) return "soixante et onze";
                    return "soixante-" + Below100(10 + u);
                case 8:
                    if (u == 0) return "quatre-vingts";
                    return "quatre-vingt-" + Small[u];
                default:
                    return "quatre-vingt-" + Below100(10 + u);
            }
        }

        private static string Below1000(int n)
        {
            if (n < 100) return Below100(n);

            int h = n / 100;
            int r = n % 100;
            string hundred = h == 1 ? "cent" : Small[h] + " cent";
            if (r == 0) return h > 1 ? hundred + "s" : hundred;
            return hundred + " " + Below100(r);
        }

        private static string Words(long n)
        {
            if (n == 0) return Small[0];

            string result = string.Empty;
            int millions = (int)(n / 1000000L);
            int thousands = (int)((n % 1000000L) / 1000L);
            int rest = (int)(n % 1000L);

            if (millions > 0)
            {
                result += millions == 1 ? "un million" : Below1000(millions) + " millions";
            }

            if (thousands > 0)
            {
                if (result.Length > 0) result += " ";
                result += thousands == 1 ? "mille" : Below1000(thousands) + " mille";
            }

            if (rest > 0)
            {
                if (result.Length > 0) result += " ";
                result += Below1000(rest);
            }

            return result;
        }
    }
}
