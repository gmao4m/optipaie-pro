using System;
using System.Windows;
using System.Windows.Resources;
using QuestPDF.Drawing;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// Registers the bundled IBM Plex faces with QuestPDF so the generated PDFs (fiche de
    /// paie, attestation, contrat, rapports) use the same type identity as the on-screen
    /// UI — on any machine, with no system font installation. The .ttf are embedded WPF
    /// resources (Assets/Fonts) and read straight from the assembly. Family only; nothing
    /// about any document's layout, values or number formatting is affected here.
    /// </summary>
    internal static class PdfFonts
    {
        /// <summary>UI/body face for PDF text (labels, headings, body).</summary>
        public const string Sans = "IBM Plex Sans";

        /// <summary>Arabic face (RTL content in PDFs).</summary>
        public const string SansArabic = "IBM Plex Sans Arabic";

        /// <summary>Monospace face for tabular figures (amounts, ids, dates).</summary>
        public const string Mono = "IBM Plex Mono";

        private static readonly string[] Files =
        {
            "IBMPlexSans-Regular", "IBMPlexSans-Medium", "IBMPlexSans-SemiBold", "IBMPlexSans-Bold",
            "IBMPlexSansArabic-Regular", "IBMPlexSansArabic-Medium", "IBMPlexSansArabic-SemiBold", "IBMPlexSansArabic-Bold",
            "IBMPlexMono-Regular", "IBMPlexMono-Medium"
        };

        private static bool _registered;

        public static void Register()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            foreach (string file in Files)
            {
                var uri = new Uri("/Assets/Fonts/" + file + ".ttf", UriKind.Relative);
                StreamResourceInfo info = Application.GetResourceStream(uri);
                if (info != null && info.Stream != null)
                {
                    FontManager.RegisterFont(info.Stream);
                }
            }
        }
    }
}
