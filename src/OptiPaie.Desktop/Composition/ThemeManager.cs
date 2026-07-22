using System;
using System.Windows;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// Switches the application between the light and dark colour themes at runtime. Every
    /// colour in the app is referenced via <c>DynamicResource</c>, so merging the dark palette
    /// on top of the light one (or removing it) re-themes the whole UI live — no restart.
    /// </summary>
    public static class ThemeManager
    {
        private const string DarkUri = "pack://application:,,,/Theme/Colors.Dark.xaml";
        private static ResourceDictionary _dark;

        /// <summary>True when the dark theme is currently applied.</summary>
        public static bool IsDark { get; private set; }

        /// <summary>Applies the light (false) or dark (true) theme.</summary>
        public static void Apply(bool dark)
        {
            Application app = Application.Current;
            if (app == null)
            {
                return;
            }

            if (dark)
            {
                if (_dark == null)
                {
                    _dark = new ResourceDictionary { Source = new Uri(DarkUri, UriKind.Absolute) };
                }
                if (!app.Resources.MergedDictionaries.Contains(_dark))
                {
                    app.Resources.MergedDictionaries.Add(_dark);
                }
            }
            else if (_dark != null)
            {
                app.Resources.MergedDictionaries.Remove(_dark);
            }

            IsDark = dark;
        }

        /// <summary>Flips between light and dark. Returns the new state.</summary>
        public static bool Toggle()
        {
            Apply(!IsDark);
            return IsDark;
        }
    }
}
