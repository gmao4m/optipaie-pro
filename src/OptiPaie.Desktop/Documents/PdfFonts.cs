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

        // FULLY-QUALIFIED pack base naming the owning assembly explicitly. A RELATIVE resource URI
        // (the old "/Assets/Fonts/x.ttf") resolves via Application.GetResourceStream against the
        // ENTRY assembly, which is null outside a normal .exe launch (test host / designer) and
        // throws "Assembly.GetEntryAssembly() returns null". Naming the assembly removes that
        // dependency, so the bundled faces load in the app and under any host.
        private static readonly string PackBase =
            "pack://application:,,,/" + typeof(PdfFonts).Assembly.GetName().Name + ";component/Assets/Fonts/";

        public static void Register()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            foreach (string file in Files)
            {
                // Fonts are for PDF rendering, not for opening the app — a single failed face must
                // never crash startup; QuestPDF simply falls back for that face.
                try
                {
                    var uri = new Uri(PackBase + file + ".ttf", UriKind.Absolute);
                    StreamResourceInfo info = Application.GetResourceStream(uri);
                    if (info != null && info.Stream != null)
                    {
                        FontManager.RegisterFont(info.Stream);
                    }
                }
                catch (Exception ex)
                {
                    Common.CrashLog.Fatal("PdfFonts.Register(" + file + ")", ex);
                }
            }
        }
    }
}
