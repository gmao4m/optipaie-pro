using OptiPaie.Localization;

namespace OptiPaie.Desktop.Localization
{
    /// <summary>
    /// Resolves a service <see cref="OptiPaie.Core.Primitives.Result"/> error to the active
    /// language: prefers the localized message for the result's stable ErrorCode, and falls
    /// back to the developer-readable French message when no translation exists.
    /// </summary>
    public static class ResultText
    {
        public static string Localize(ILocalizationService localization, string error, string errorCode)
        {
            if (localization != null && !string.IsNullOrEmpty(errorCode))
            {
                string localized = localization.GetString(errorCode);
                if (!string.IsNullOrEmpty(localized) && localized != errorCode)
                {
                    return localized;
                }
            }

            return error;
        }
    }
}
