using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Certificates;
using OptiPaie.Services.Certificates;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Phase C proof harness — renders the ATS/DRT overlay onto the blank-form scans so the
    /// mm placement can be verified visually and calibrated. Reads form-layout.json at RUNTIME,
    /// so nudging a coordinate re-renders without recompiling. Not a pass/fail unit test.
    /// </summary>
    [TestFixture, Explicit]
    public sealed class AtsDrtProofHarness
    {
        private static readonly string Repo = @"C:\Users\PC\Desktop\OptiPaie PRO";
        private static readonly string Layout =
            Path.Combine(Repo, "src", "OptiPaie.Desktop", "Assets", "AtsDrtTemplates", "form-layout.json");
        private static readonly string Forms =
            @"C:\Users\PC\AppData\Local\Temp\claude\C--Users-PC-Desktop-OptiPaie-PRO\41f83509-89aa-418a-93d7-98a06fa73bb7\scratchpad\forms";

        private static Dictionary<string, string> AtsNormal() => new Dictionary<string, string>
        {
            ["NMEMPLOYEUR"] = "BENSALEM Ahmed",
            ["NEMPLOYEUR"] = "09 102 457 89",
            ["RS"] = "SARL Atlas Industrie",
            ["EADRESSE"] = "Zone industrielle, Lot 24, Boufarik, Blida",
            ["NMS"] = "BENALI Karim",
            ["NSS"] = "88 0412 1234 56",
            ["NEA"] = "Blida",
            ["POSTE"] = "Comptable",
            ["SADRESSE"] = "Cite 200 logements, Bt C, Blida",
            ["DATEN"] = "120385",
            ["DATER"] = "010116",
            ["DATEAT"] = "150925",
            ["DATEREPRISE"] = "011025",
            ["DATEAUJRH"] = ""
        };

        private static Dictionary<string, string> AtsExtreme() => new Dictionary<string, string>
        {
            ["NMEMPLOYEUR"] = "ABDERRAHMANE-BOUMEDIENE Mohammed El Amine",
            ["NEMPLOYEUR"] = "09 102 457 89",
            ["RS"] = "SARL Etablissements Industriels Reunis du Centre Algerien SPA",
            ["EADRESSE"] = "Zone industrielle numero 3, Lot 124 bis, Route nationale 1, Boufarik, Wilaya de Blida",
            ["NMS"] = "BOUABDALLAH-MEZIANE Abdelnour Cherif",
            ["NSS"] = "88 0412 1234 56",
            ["NEA"] = "Sidi Bel Abbes",
            ["POSTE"] = "Responsable administratif et financier principal",
            ["SADRESSE"] = "Cite des 1200 logements sociaux participatifs, Batiment D4, Escalier 2, Blida",
            ["DATEN"] = "120385",
            ["DATER"] = "010116",
            ["DATEAT"] = "150925",
            ["DATEREPRISE"] = "011025",
            ["DATEAUJRH"] = ""
        };

        private static Dictionary<string, string> DrtResumed() => new Dictionary<string, string>
        {
            ["NMS"] = "BENALI", ["NMP"] = "Karim", ["NSS"] = "88 0412 1234 56",
            ["DATEN"] = "120385", ["NEA"] = "Blida", ["DATEAT"] = "150925",
            ["Case1"] = "X", ["DATEREPRISE"] = "011025",
            ["LIEU"] = "Blida", ["AUJOURDHUI"] = "02/10/2025"
        };

        private static Dictionary<string, string> DrtNotResumed() => new Dictionary<string, string>
        {
            ["NMS"] = "BENALI", ["NMP"] = "Karim", ["NSS"] = "88 0412 1234 56",
            ["DATEN"] = "120385", ["NEA"] = "Blida", ["DATEAT"] = "150925",
            ["Case2"] = "X", ["DATEAUJRH"] = "300925",
            ["LIEU"] = "Blida", ["AUJOURDHUI"] = "30/09/2025",
            ["NMS2"] = "BENALI", ["NMP2"] = "Karim", ["NSS2"] = "88 0412 1234 56",
            ["DATEAUJRH2"] = "300925", ["LIEU1"] = "Blida", ["AUJOURDHUI2"] = "30/09/2025"
        };

        // Merge page-1 identity + page-2 table into one dict (RenderPdf uses one dict for all pages).
        private static Dictionary<string, string> AtsFull()
        {
            var v = AtsNormal();
            foreach (var kv in AtsPage2Normal()) v[kv.Key] = kv.Value;
            return v;
        }

        private static Dictionary<string, string> AtsPage2Arabic()
        {
            // Algerian Arabic month names — exactly what CertificateService emits when the UI is Arabic.
            string[] ar = { "جانفي 2025", "فيفري 2025", "مارس 2025", "أفريل 2025", "ماي 2025", "جوان 2025" };
            var v = new Dictionary<string, string> { ["LIEU"] = "البليدة", ["AUJOURDHUI"] = "23/08/2026" };
            for (int i = 0; i < 12; i++)
            {
                int n = i + 1; bool on = i < 6;
                v[$"M{n}"] = on ? ar[i] : "";
                v[$"JT{n}"] = on ? "22" : "/";
                v[$"MT{n}"] = on ? (i == 0 ? "مرض" : "") : "/";   // "maladie" in Arabic
                v[$"SS{n}"] = on ? "45000,00 DA" : "/";
                v[$"PO{n}"] = on ? "4050,00 DA" : "/";
            }
            return v;
        }

        [Test]
        public void Print_ATS_Arabic_Months()
        {
            FormLayoutConfig cfg = AtsDrtFormRenderer.LoadLayout(Layout);
            FormPage page2 = cfg.Forms["ATS"].Pages[1];
            string bg2 = Path.Combine(Forms, page2.BackgroundImage);
            AtsDrtFormRenderer.RenderPrintPreviewPng(page2, AtsPage2Arabic(), bg2, 0, 0, 200, Path.Combine(Forms, "print_ATS_arabic.png"));
            Assert.Pass();
        }

        [Test]
        public void Print_ATS_and_DRT()
        {
            FormLayoutConfig cfg = AtsDrtFormRenderer.LoadLayout(Layout);
            FormDefinition ats = cfg.Forms["ATS"];
            FormDefinition drt = cfg.Forms["DRT"];

            // 1) The REAL production output: the exact PDFs a client prints (values only, black, no
            //    background) — printed on top of the blank pre-printed CNAS form.
            AtsDrtFormRenderer.RenderPdf(ats, AtsFull(), 0, 0, Path.Combine(Forms, "PRINT_ATS.pdf"));
            AtsDrtFormRenderer.RenderPdf(drt, DrtNotResumed(), 0, 0, Path.Combine(Forms, "PRINT_DRT.pdf"));

            // 2) Black-ink-on-form previews at 200 dpi — what the finished paper looks like.
            string bg1 = Path.Combine(Forms, ats.Pages[0].BackgroundImage);
            string bg2 = Path.Combine(Forms, ats.Pages[1].BackgroundImage);
            string bgD = Path.Combine(Forms, drt.Pages[0].BackgroundImage);
            AtsDrtFormRenderer.RenderPrintPreviewPng(ats.Pages[0], AtsFull(), bg1, 0, 0, 200, Path.Combine(Forms, "print_ATS_page1.png"));
            AtsDrtFormRenderer.RenderPrintPreviewPng(ats.Pages[1], AtsFull(), bg2, 0, 0, 200, Path.Combine(Forms, "print_ATS_page2.png"));
            AtsDrtFormRenderer.RenderPrintPreviewPng(drt.Pages[0], DrtNotResumed(), bgD, 0, 0, 200, Path.Combine(Forms, "print_DRT.png"));

            Assert.That(File.Exists(Path.Combine(Forms, "PRINT_ATS.pdf")) && File.Exists(Path.Combine(Forms, "PRINT_DRT.pdf")), Is.True);
        }

        [Test]
        public void Render_Calibration_Sheet()
        {
            AtsDrtFormRenderer.RenderCalibrationProofPng(0, 0, 200, Path.Combine(Forms, "proof_calibration.png"));
            AtsDrtFormRenderer.RenderCalibrationProofPng(5, 3, 200, Path.Combine(Forms, "proof_calibration_offset.png"));
            Assert.Pass();
        }

        [Test]
        public void Render_DRT_Proofs()
        {
            FormLayoutConfig cfg = AtsDrtFormRenderer.LoadLayout(Layout);
            FormPage page = cfg.Forms["DRT"].Pages[0];
            string bg = Path.Combine(Forms, page.BackgroundImage);
            AtsDrtFormRenderer.RenderProofPng(page, DrtResumed(), bg, 0, 0, 300, Path.Combine(Forms, "proof_DRT_resumed.png"));
            AtsDrtFormRenderer.RenderProofPng(page, DrtNotResumed(), bg, 0, 0, 300, Path.Combine(Forms, "proof_DRT_notresumed.png"));
            Assert.Pass();
        }

        // Simulates CertificateBookmarkMapper.MapAts' 12-month table: `active` real months,
        // the rest printed as "/" (Mois column blank), exactly like production.
        private static void FillMonths(Dictionary<string, string> v, int active, bool extreme)
        {
            string[] months = { "Janvier 2025", "Février 2025", "Mars 2025", "Avril 2025", "Mai 2025",
                "Juin 2025", "Juillet 2025", "Août 2025", "Septembre 2025", "Octobre 2025",
                "Novembre 2025", "Décembre 2025" };
            for (int i = 0; i < 12; i++)
            {
                int n = i + 1;
                bool on = i < active;
                v[$"M{n}"] = on ? months[i] : "";
                v[$"JT{n}"] = on ? (extreme ? "26" : "22") : "/";
                v[$"MT{n}"] = on ? (extreme ? "Maladie longue durée prolongée avec hospitalisation puis congé annuel et récupération heures" : (i % 3 == 0 ? "Maladie" : "")) : "/";
                v[$"SS{n}"] = on ? (extreme ? "1250000,00 DA" : "45000,00 DA") : "/";
                v[$"PO{n}"] = on ? (extreme ? "112500,00 DA" : "4050,00 DA") : "/";
            }
        }

        private static Dictionary<string, string> AtsPage2Normal()
        {
            var v = new Dictionary<string, string> { ["LIEU"] = "Blida", ["AUJOURDHUI"] = "23/08/2026" };
            FillMonths(v, 6, false);
            return v;
        }

        private static Dictionary<string, string> AtsPage2Extreme()
        {
            var v = new Dictionary<string, string> { ["LIEU"] = "Sidi Bel Abbes", ["AUJOURDHUI"] = "23/08/2026" };
            FillMonths(v, 12, true);
            return v;
        }

        [Test]
        public void Render_ATS_Proofs()
        {
            FormLayoutConfig cfg = AtsDrtFormRenderer.LoadLayout(Layout);
            FormDefinition ats = cfg.Forms["ATS"];
            FormPage page1 = ats.Pages[0];
            FormPage page2 = ats.Pages[1];
            string bg1 = Path.Combine(Forms, page1.BackgroundImage);
            string bg2 = Path.Combine(Forms, page2.BackgroundImage);

            AtsDrtFormRenderer.RenderProofPng(page1, AtsNormal(), bg1, 0, 0, 300, Path.Combine(Forms, "proof_ATS1_normal.png"));
            AtsDrtFormRenderer.RenderProofPng(page1, AtsExtreme(), bg1, 0, 0, 300, Path.Combine(Forms, "proof_ATS1_extreme.png"));
            AtsDrtFormRenderer.RenderProofPng(page2, AtsPage2Normal(), bg2, 0, 0, 300, Path.Combine(Forms, "proof_ATS2_normal.png"));
            AtsDrtFormRenderer.RenderProofPng(page2, AtsPage2Extreme(), bg2, 0, 0, 300, Path.Combine(Forms, "proof_ATS2_extreme.png"));
            Assert.Pass();
        }
    }
}
