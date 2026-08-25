using System;
using System.Reflection;
using QuestPDF.Drawing;

namespace OptiPaie.Services.Documents
{
    /// <summary>
    /// Registers the bundled IBM Plex Sans Arabic faces (which carry BOTH Latin and Arabic glyphs)
    /// with QuestPDF, straight from embedded resources in this assembly. Because the font travels
    /// inside the DLL and is embedded into every generated PDF, the bilingual documents render
    /// identically in the desktop app AND in unit tests / CI — never depending on a font being
    /// installed on the machine, so an Arabic glyph is never a missing-glyph box.
    /// Idempotent and thread-safe.
    /// </summary>
    public static class DocumentFonts
    {
        public const string Arabic = "IBM Plex Sans Arabic";

        private static readonly object Gate = new object();
        private static bool _registered;

        public static void EnsureRegistered()
        {
            if (_registered) return;
            lock (Gate)
            {
                if (_registered) return;
                Assembly asm = typeof(DocumentFonts).Assembly;
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = asm.GetManifestResourceStream(name))
                    {
                        if (stream != null) FontManager.RegisterFont(stream);
                    }
                }
                _registered = true;
            }
        }
    }
}
