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
        // FULLY-QUALIFIED pack URI that names the owning assembly explicitly (built from the real
        // assembly name, so it survives a rename). The short "pack://application:,,,/Theme/..." form
        // resolves against the ENTRY assembly, which is null outside a normal .exe launch (a test
        // host, a designer, a detached context) — there it throws
        // "Assembly.GetEntryAssembly() returns null". Naming the assembly removes that dependency
        // entirely, so the dark palette loads identically in the app and under any host.
        private static readonly string DarkUri =
            "pack://application:,,,/" + typeof(ThemeManager).Assembly.GetName().Name + ";component/Theme/Colors.Dark.xaml";
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

            try
            {
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
            catch (Exception ex)
            {
                // The dark palette is a PREFERENCE, not a requirement: a resource-load failure must
                // never take down startup. Fall back to the light theme and record why.
                Common.CrashLog.Fatal("ThemeManager.Apply(dark=" + dark + ")", ex);
                IsDark = false;
            }
        }

        /// <summary>Flips between light and dark. Returns the new state.</summary>
        public static bool Toggle()
        {
            Apply(!IsDark);
            return IsDark;
        }
    }
}
