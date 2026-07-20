using System;
using OptiPaie.Localization;

namespace OptiPaie.App.Common
{
    /// <summary>
    /// Resolves a localized display string for an enum value using the convention
    /// "Enum_{TypeName}_{ValueName}", falling back to the value name if no resource
    /// exists. Keeps enum display text fully bilingual with no raw English.
    /// </summary>
    public static class EnumLocalizer
    {
        public static string Localize(ILocalizationService localization, Enum value)
        {
            string key = "Enum_" + value.GetType().Name + "_" + value;
            string text = localization.GetString(key);
            return text == key ? value.ToString() : text;
        }
    }
}
