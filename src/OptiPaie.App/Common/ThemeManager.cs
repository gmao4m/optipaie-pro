using System;
using System.Collections.Generic;
using DevExpress.LookAndFeel;

namespace OptiPaie.App.Common
{
    /// <summary>
    /// Theme manager. OptiPaie DZ ships a SINGLE, professionally tuned light skin so
    /// the interface is always 100% readable and visually consistent. The premium
    /// design is light-first (white surfaces, dark text); a dark skin would leave the
    /// skin's default white text on those white surfaces (white-on-white), so dark and
    /// office skins are intentionally not offered. <see cref="Apply"/> therefore always
    /// applies the light skin regardless of the requested key — no code path, saved
    /// setting or user action can ever switch the app into an unreadable skin.
    /// </summary>
    public static class ThemeManager
    {
        public const string Light = "light";

        /// <summary>The one skin the whole product renders with (Win11-style, light).</summary>
        public const string LightSkin = "WXI";

        public static IEnumerable<string> Keys
        {
            get { yield return Light; }
        }

        public static void Apply(string themeKey)
        {
            // Single source of truth: always the light skin, whatever is requested.
            UserLookAndFeel.Default.SetSkinStyle(LightSkin);
        }
    }
}
