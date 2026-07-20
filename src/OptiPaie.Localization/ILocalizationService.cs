using System;
using System.Globalization;

namespace OptiPaie.Localization
{
    /// <summary>
    /// Manages the active UI language and resolves localized strings. The UI
    /// subscribes to <see cref="LanguageChanged"/> to re-apply text and layout
    /// direction instantly when the language switches.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>The active language code ("fr" / "ar").</summary>
        string CurrentLanguage { get; }

        /// <summary>The active culture.</summary>
        CultureInfo CurrentCulture { get; }

        /// <summary>True when the active language is right-to-left (Arabic).</summary>
        bool IsRightToLeft { get; }

        /// <summary>Raised after the language changes.</summary>
        event EventHandler LanguageChanged;

        /// <summary>Switches the active language and raises <see cref="LanguageChanged"/>.</summary>
        void SetLanguage(string code);

        /// <summary>Returns the localized string for a key, or the key itself if missing.</summary>
        string GetString(string key);

        /// <summary>Returns a localized, formatted string for a key.</summary>
        string Format(string key, params object[] args);
    }
}
