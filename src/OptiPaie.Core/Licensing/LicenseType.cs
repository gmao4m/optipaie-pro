using System;

namespace OptiPaie.Core.Licensing
{
    /// <summary>The commercial type of a license.</summary>
    public enum LicenseType
    {
        Unknown = 0,
        Trial = 1,
        Lifetime = 2,
        Annual = 3,
        Monthly = 4,
        Demo = 5,
        Enterprise = 6
    }

    /// <summary>Parsing and display helpers for <see cref="LicenseType"/>.</summary>
    public static class LicenseTypes
    {
        /// <summary>Parses the server's lowercase type string into <see cref="LicenseType"/>.</summary>
        public static LicenseType Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return LicenseType.Unknown;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "trial": return LicenseType.Trial;
                case "lifetime": return LicenseType.Lifetime;
                case "annual": return LicenseType.Annual;
                case "monthly": return LicenseType.Monthly;
                case "demo": return LicenseType.Demo;
                case "enterprise": return LicenseType.Enterprise;
                default: return LicenseType.Unknown;
            }
        }

        /// <summary>A French label for the UI.</summary>
        public static string DisplayName(LicenseType type)
        {
            switch (type)
            {
                case LicenseType.Trial: return "Essai";
                case LicenseType.Lifetime: return "Permanente";
                case LicenseType.Annual: return "Annuelle";
                case LicenseType.Monthly: return "Mensuelle";
                case LicenseType.Demo: return "Démonstration";
                case LicenseType.Enterprise: return "Entreprise";
                default: return "—";
            }
        }
    }
}
