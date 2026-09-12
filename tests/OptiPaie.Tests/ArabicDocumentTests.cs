using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OptiPaie.Common.Text;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Services.Documents;
using QuestPDF.Fluent;
using SkiaSharp;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Non-regression for the Arabic documents (attestation de travail / salaire / certificat).
    /// Two failures the client actually hit are guarded here:
    ///   • the embedded Arabic font missing a glyph used by the document → "????"/□ on the printout;
    ///   • the bilingual attestation failing to render at all (missing font, mixed-run crash…).
    /// Plus the digit-order fix (<see cref="ArabicText.FixRtlDigits"/>) that keeps numbers/dates
    /// correct inside Arabic text.
    /// </summary>
    [TestFixture]
    public sealed class ArabicDocumentTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-ar-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // Every Arabic string the attestation puts on the page (labels, titles, prose) plus a
        // representative Arabic name / société / adresse. If the embedded font ever lacks one of
        // these glyphs, the printout shows a missing-glyph box — this test fails first.
        private const string ArabicCoverage =
            "شهادة عمل شهادة نهاية عمل شهادة أجر شهادة " +
            "نشهد نحن الموقّعون أدناه بما يلي : " +
            "الاسم واللقب رقم الضمان الاجتماعي الوظيفة تاريخ التوظيف تاريخ نهاية العلاقة الأقدمية الأجر الشهري القاعدي " +
            "سُلّمت هذه الشهادة للمعني(ة) بالأمر قصد استعمالها عند الحاجة. " +
            "حُرّر يوم : الإدارة " +
            "عبد الرحمن بومدين محمد الأمين الشريف " +
            "شركة أطلس للصناعة ذات مسؤولية محدودة " +
            "المنطقة الصناعية، رقم 24، بوفاريك، ولاية البليدة سنة أشهر";

        [Test]
        public void EmbeddedArabicFont_ContainsEveryGlyphTheAttestationUses()
        {
            byte[] ttf = LoadEmbeddedArabicFont();
            var missing = new List<string>();

            using (var data = SKData.CreateCopy(ttf))
            using (var tf = SKTypeface.FromData(data))
            {
                Assert.That(tf, Is.Not.Null, "embedded Arabic font could not be loaded");
                foreach (char c in ArabicCoverage)
                {
                    if (char.IsWhiteSpace(c)) continue;
                    if (tf.GetGlyph(c) == 0) // 0 = .notdef → the font has no glyph for this character
                        missing.Add("U+" + ((int)c).ToString("X4") + " '" + c + "'");
                }
            }

            Assert.That(missing, Is.Empty,
                "The embedded Arabic font is missing glyphs (would print as '????'/□): " + string.Join(", ", missing));
        }

        [Test]
        public void Attestation_RendersArabic_WithoutError_AndEmbedsTheArabicFont()
        {
            foreach (CertificateType type in new[] { CertificateType.WorkCertificate, CertificateType.SalaryCertificate, CertificateType.WorkExperience })
            {
                CertificateRenderModel model = ArabicModel(type);
                var doc = new WorkCertificateDocument(model);

                string path = Path.Combine(_dir, type + ".pdf");
                Assert.DoesNotThrow(() => Document.Create(doc.Compose).GeneratePdf(path),
                    "The bilingual attestation must render the Arabic content without throwing (" + type + ").");

                byte[] bytes = File.ReadAllBytes(path);
                Assert.That(bytes.Length, Is.GreaterThan(2000), type + " PDF is trivially small");
                // The Arabic-capable font must be embedded in the PDF (QuestPDF subsets it) — otherwise
                // an Arabic glyph would fall back to a machine font or a missing-glyph box.
                Assert.That(ContainsAscii(bytes, "PlexSansArabic"), Is.True,
                    type + " PDF does not embed the Arabic font (Arabic would render as boxes).");
            }
        }

        // ── ArabicText.FixRtlDigits : the digit-order compensation ──

        [Test]
        public void FixRtlDigits_ReversesDigitRuns_OnlyInArabicStrings()
        {
            // Pure Latin / pure numeric: untouched (no Arabic → no RTL reversal to compensate).
            Assert.That(ArabicText.FixRtlDigits("15/03/2018"), Is.EqualTo("15/03/2018"));
            Assert.That(ArabicText.FixRtlDigits("Zone 24, Blida"), Is.EqualTo("Zone 24, Blida"));

            // Arabic + number: the digit run is pre-reversed so QuestPDF's naive RTL reversal restores it.
            Assert.That(ArabicText.FixRtlDigits("رقم 24"), Is.EqualTo("رقم 42"));
            Assert.That(ArabicText.FixRtlDigits("منذ 15/03/2018"), Is.EqualTo("منذ 8102/30/51"));
            Assert.That(ArabicText.FixRtlDigits("12 سنة"), Is.EqualTo("21 سنة"));
            // Multiple separate digit runs (a company name like "20 أوت 1955") — each run reversed independently.
            Assert.That(ArabicText.FixRtlDigits("شركة 20 أوت 1955"), Is.EqualTo("شركة 02 أوت 5591"));

            // Applying it twice is NOT idempotent by design (it is a display-layer compensation,
            // applied exactly once at render time) — but a double application round-trips.
            string once = ArabicText.FixRtlDigits("رقم 24");
            Assert.That(ArabicText.FixRtlDigits(once), Is.EqualTo("رقم 24"));
        }

        [Test]
        public void ContainsArabic_DetectsArabicScript()
        {
            Assert.That(ArabicText.ContainsArabic("Blida 16000"), Is.False);
            Assert.That(ArabicText.ContainsArabic(""), Is.False);
            Assert.That(ArabicText.ContainsArabic(null), Is.False);
            Assert.That(ArabicText.ContainsArabic("البليدة"), Is.True);
            Assert.That(ArabicText.ContainsArabic("SARL أطلس"), Is.True);
        }

        // ── B4 : a « document libre » must PRINT the body text (it was silently ignored) ──
        [Test]
        public void CustomDocument_PrintsTheBodyText_NotAGenericAttestation()
        {
            CertificateRenderModel withBody = ArabicModel(CertificateType.Custom);
            withBody.Certificate.Body = "Objet : attestation de prise en charge.\nCeci est le corps libre saisi par l'utilisateur.";

            CertificateRenderModel empty = ArabicModel(CertificateType.Custom);
            empty.Certificate.Body = string.Empty;

            string p1 = Path.Combine(_dir, "custom_body.pdf");
            string p2 = Path.Combine(_dir, "custom_empty.pdf");
            Assert.DoesNotThrow(() => Document.Create(new WorkCertificateDocument(withBody).Compose).GeneratePdf(p1));
            Assert.DoesNotThrow(() => Document.Create(new WorkCertificateDocument(empty).Compose).GeneratePdf(p2));

            byte[] a = File.ReadAllBytes(p1), b = File.ReadAllBytes(p2);
            Assert.That(a, Is.Not.EqualTo(b), "the free body must change the output — it must be printed, not ignored");
            Assert.That(a.Length, Is.GreaterThan(b.Length), "the document that carries a body is larger");
        }

        // ── helpers ──

        private static byte[] LoadEmbeddedArabicFont()
        {
            Assembly asm = typeof(DocumentFonts).Assembly;
            string name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.IndexOf("Arabic", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                     n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
            Assert.That(name, Is.Not.Null, "the Arabic font is not embedded in OptiPaie.Services");

            using (Stream s = asm.GetManifestResourceStream(name))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static CertificateRenderModel ArabicModel(CertificateType type) => new CertificateRenderModel
        {
            Company = new Company
            {
                NameFr = "SARL ATLAS INDUSTRIE",
                NameAr = "شركة 20 أوت 1955 للصناعة",           // digit-bearing Arabic name (guards the NameAr digit fix)
                AddressFr = "Zone Industrielle, Lot 24, Boufarik, Blida",
                AddressAr = "المنطقة الصناعية، رقم 24، بوفاريك، ولاية البليدة",
                Rc = "16/00-1234567 B 09", Nif = "099816010001234",
                City = "عنابة"                                   // Arabic city (guards the isolated-city signature fix)
            },
            Employee = new Employee
            {
                LastNameFr = "ABDERRAHMANE-BOUMEDIENE", FirstNameFr = "Mohammed El Amine",
                LastNameAr = "عبد الرحمن بومدين", FirstNameAr = "محمد الأمين الشريف",
                Nss = "88 0412 1234 56", Poste = "Responsable administratif et financier",
                HireDate = new DateTime(2018, 3, 15), ExitDate = new DateTime(2026, 8, 31)
            },
            Certificate = new WorkCertificate { Type = type, Reference = "ATT-2026-0042", IssueDate = new DateTime(2026, 9, 8), Purpose = "قصد تقديمها للبنك" },
            MonthlySalary = 85000m, SeniorityYears = 8, SeniorityMonths = 5
        };

        private static bool ContainsAscii(byte[] haystack, string needle)
        {
            byte[] pat = System.Text.Encoding.ASCII.GetBytes(needle);
            for (int i = 0; i + pat.Length <= haystack.Length; i++)
            {
                int j = 0;
                while (j < pat.Length && haystack[i + j] == pat[j]) j++;
                if (j == pat.Length) return true;
            }
            return false;
        }
    }
}
