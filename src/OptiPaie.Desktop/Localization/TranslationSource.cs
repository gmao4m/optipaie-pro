using System;
using System.ComponentModel;
using OptiPaie.Localization;

namespace OptiPaie.Desktop.Localization
{
    /// <summary>
    /// A single bindable source of localized strings for the WPF UI. XAML binds to its
    /// string indexer (via <see cref="LocExtension"/>); when the language changes it raises
    /// an indexer-changed notification so every localized binding re-reads its value live —
    /// no window reload required. The <see cref="ILocalizationService"/> stays the single
    /// source of truth; this only bridges it to data binding.
    /// </summary>
    public sealed class TranslationSource : INotifyPropertyChanged
    {
        private static readonly TranslationSource _instance = new TranslationSource();

        private ILocalizationService _localization;

        private TranslationSource()
        {
        }

        public static TranslationSource Instance => _instance;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Connects the source to the localization service and refreshes on language change.</summary>
        public void Attach(ILocalizationService localization)
        {
            if (_localization != null)
            {
                _localization.LanguageChanged -= OnLanguageChanged;
            }

            _localization = localization;

            if (_localization != null)
            {
                _localization.LanguageChanged += OnLanguageChanged;
            }

            Refresh();
        }

        /// <summary>The localized string for a key (falls back to the key when unresolved).</summary>
        public string this[string key] => _localization != null ? _localization.GetString(key) : key;

        private void OnLanguageChanged(object sender, EventArgs e) => Refresh();

        /// <summary>Notifies every indexer binding to re-read its localized value.</summary>
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
