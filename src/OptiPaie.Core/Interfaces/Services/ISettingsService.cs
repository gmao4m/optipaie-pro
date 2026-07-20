namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>Reads and writes UI/application preferences (the Settings table).</summary>
    public interface ISettingsService
    {
        /// <summary>Current UI language code ("fr" / "ar").</summary>
        string GetLanguage();

        /// <summary>Sets the UI language code.</summary>
        void SetLanguage(string code);

        /// <summary>Current DevExpress theme/skin name.</summary>
        string GetTheme();

        /// <summary>Sets the theme/skin name.</summary>
        void SetTheme(string theme);

        /// <summary>The default company id pre-selected in the UI, or null.</summary>
        long? GetDefaultCompanyId();

        /// <summary>Sets (or clears) the default company id.</summary>
        void SetDefaultCompanyId(long? companyId);

        /// <summary>The default overtime majoration as a fraction (e.g. 0.5).</summary>
        decimal GetOvertimeMajoration();

        /// <summary>Reads a raw setting value, returning a default when absent.</summary>
        string Get(string key, string defaultValue = null);

        /// <summary>Writes a raw setting value.</summary>
        void Set(string key, string value);
    }
}
