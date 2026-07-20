using System;
using System.Globalization;
using System.Resources;
using System.Threading;
using OptiPaie.Common.Constants;

namespace OptiPaie.Localization
{
    /// <summary>
    /// Default <see cref="ILocalizationService"/>: maps the language code to an
    /// Algerian culture (ar-DZ / fr-FR), applies it to the current thread, exposes
    /// the layout direction and resolves strings from the embedded resources.
    /// <para>
    /// String resources live in <c>Resources/Strings.resx</c> (French, neutral) and
    /// <c>Resources/Strings.ar.resx</c> (Arabic). Missing keys fall back to the key
    /// name, so the application never fails because of a missing translation.
    /// </para>
    /// </summary>
    public sealed class LocalizationService : ILocalizationService
    {
        private const string ResourceBaseName = "OptiPaie.Localization.Resources.Strings";

        private readonly ResourceManager _resourceManager;
        private CultureInfo _currentCulture;

        public event EventHandler LanguageChanged;

        public LocalizationService()
        {
            _resourceManager = new ResourceManager(ResourceBaseName, typeof(LocalizationService).Assembly);
            _currentCulture = ResolveCulture(AppConstants.DefaultLanguage);
        }

        public string CurrentLanguage => _currentCulture.TwoLetterISOLanguageName;

        public CultureInfo CurrentCulture => _currentCulture;

        public bool IsRightToLeft => _currentCulture.TextInfo.IsRightToLeft;

        public void SetLanguage(string code)
        {
            _currentCulture = ResolveCulture(code);

            Thread.CurrentThread.CurrentUICulture = _currentCulture;
            Thread.CurrentThread.CurrentCulture = _currentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _currentCulture;
            CultureInfo.DefaultThreadCurrentCulture = _currentCulture;

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            try
            {
                string value = _resourceManager.GetString(key, _currentCulture);
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch (MissingManifestResourceException)
            {
                return key;
            }
        }

        public string Format(string key, params object[] args)
        {
            string format = GetString(key);

            try
            {
                return string.Format(_currentCulture, format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        private static CultureInfo ResolveCulture(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                code = AppConstants.DefaultLanguage;
            }

            switch (code.Trim().ToLowerInvariant())
            {
                case AppConstants.LanguageArabic:
                    return CultureInfo.GetCultureInfo("ar-DZ");
                case AppConstants.LanguageFrench:
                    return CultureInfo.GetCultureInfo("fr-FR");
                default:
                    return CultureInfo.GetCultureInfo(code);
            }
        }
    }
}
