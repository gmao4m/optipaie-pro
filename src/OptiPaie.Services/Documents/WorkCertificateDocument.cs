using System;
using System.Collections.Generic;
using System.Globalization;
using OptiPaie.Common.Text;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Services.Documents
{
    /// <summary>
    /// Bilingual (French + Arabic) work / experience / salary certificate — attestation de travail.
    ///
    /// Built to survive QuestPDF 2022.12's lack of real Unicode bidi (see <see cref="ArabicText"/>):
    /// every dynamic value sits in its OWN text element (so an Arabic name, société or adresse keeps
    /// its correct right-to-left, joined shaping and is never reversed by being mixed with Latin), the
    /// Arabic prose is pure Arabic, and every Arabic string that carries digits/dates is passed through
    /// <see cref="ArabicText.FixRtlDigits"/> so numbers read correctly. The font is the embedded
    /// IBM Plex Sans Arabic (registered by <see cref="DocumentFonts"/>), so no glyph is ever a "□"/"????".
    ///
    /// Lives in OptiPaie.Services (not the WPF app) so it renders identically in the app, in unit tests
    /// and in CI — and so a non-regression test can prove the Arabic renders without missing glyphs.
    /// </summary>
    public sealed class WorkCertificateDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        // One face for the whole document: IBM Plex Sans Arabic carries BOTH Latin and Arabic glyphs
        // and is the only font embedded in this assembly, so the certificate renders identically in
        // the app, in unit tests and in CI without depending on any machine-installed font.
        private const string FontAr = "IBM Plex Sans Arabic";

        private readonly CertificateRenderModel _model;

        public WorkCertificateDocument(CertificateRenderModel model)
        {
            _model = model;
        }

        public void Compose(IDocumentContainer container)
        {
            DocumentFonts.EnsureRegistered();

            Company company = _model.Company ?? new Company();
            Employee employee = _model.Employee ?? new Employee();
            WorkCertificate certificate = _model.Certificate ?? new WorkCertificate();

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                // Default to the Arabic-capable face everywhere so an Arabic value never falls back.
                page.DefaultTextStyle(t => t.FontFamily(FontAr).FontSize(11.5f).LineHeight(1.4f));

                page.Header().Column(col => Header(col, company));
                page.Content().PaddingVertical(18).Column(col => Body(col, employee, certificate));
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("OptiPaie PRO — ").FontSize(8).FontColor("#999");
                    t.Span(company.NameFr ?? string.Empty).FontSize(8).FontColor("#999");
                });
            });
        }

        // ── header : company identity, French then Arabic, each value in its OWN element ──
        private static void Header(ColumnDescriptor col, Company company)
        {
            if (!string.IsNullOrWhiteSpace(company.NameFr))
                col.Item().Text(company.NameFr).FontSize(15).SemiBold();
            if (!string.IsNullOrWhiteSpace(company.NameAr))
                col.Item().AlignRight().Text(ArabicText.FixRtlDigits(company.NameAr)).FontSize(13).SemiBold();

            if (!string.IsNullOrWhiteSpace(company.AddressFr))
                col.Item().Text(company.AddressFr).FontSize(9).FontColor("#555");
            if (!string.IsNullOrWhiteSpace(company.AddressAr))
                col.Item().AlignRight().Text(ArabicText.FixRtlDigits(company.AddressAr)).FontSize(9).FontColor("#555");

            string ids = CompanyIds(company);
            if (ids.Length > 0)
                col.Item().Text(ids).FontSize(9).FontColor("#555");

            col.Item().PaddingTop(6).LineHorizontal(1).LineColor("#333");
        }

        private void Body(ColumnDescriptor col, Employee employee, WorkCertificate certificate)
        {
            // ── bilingual title ──
            col.Item().PaddingTop(6).AlignCenter().Text(HeadingFr(certificate.Type)).FontSize(16).SemiBold();
            col.Item().AlignCenter().Text(HeadingAr(certificate.Type)).FontSize(15).SemiBold();
            if (!string.IsNullOrWhiteSpace(certificate.Reference))
                col.Item().AlignCenter().PaddingTop(2).Text("Réf. : " + certificate.Reference).FontSize(9).FontColor("#555");

            if (certificate.Type == CertificateType.Custom)
            {
                // Document libre : on imprime le TEXTE saisi par l'utilisateur (obligatoire pour ce
                // type) — il était auparavant silencieusement ignoré. Bilingue-safe : chaque paragraphe
                // dans son propre élément, les paragraphes arabes alignés à droite et chiffres corrigés.
                FreeBody(col, certificate.Body);
            }
            else
            {
                // ── bilingual intro (fixed text — no inline dynamic value, so nothing to mangle) ──
                col.Item().PaddingTop(18).Text("Nous soussignés attestons ce qui suit :");
                col.Item().AlignRight().Text("نشهد نحن الموقّعون أدناه بما يلي :");

                // ── identity card : every value isolated in its own element ──
                col.Item().PaddingTop(12).Border(0.8f).BorderColor("#bbbbbb").Padding(12).Column(card =>
                {
                    card.Spacing(7);
                    Field(card, "Nom & prénom", FullName(employee), "الاسم واللقب");
                    if (!string.IsNullOrWhiteSpace(employee.Nss))
                        Field(card, "N° sécurité sociale", employee.Nss, "رقم الضمان الاجتماعي");
                    Field(card, "Fonction", Poste(employee), "الوظيفة");
                    Field(card, "Date de recrutement", D(employee.HireDate), "تاريخ التوظيف");

                    if (certificate.Type == CertificateType.WorkExperience && employee.ExitDate.HasValue)
                        Field(card, "Date de fin de relation", D(employee.ExitDate.Value), "تاريخ نهاية العلاقة");

                    string sfr = SeniorityFr();
                    if (sfr.Length > 0)
                        Field(card, "Ancienneté", sfr, "الأقدمية");

                    if (certificate.Type == CertificateType.SalaryCertificate)
                        Field(card, "Salaire mensuel de base", _model.MonthlySalary.ToString("N2", Fr) + " DA", "الأجر الشهري القاعدي");
                });

                // ── bilingual closing (fixed text) ──
                // A Latin purpose is fine inline; an Arabic purpose must be its OWN element (never mixed
                // with the French prose) and pass through FixRtlDigits, per the document invariant.
                string purpose = string.IsNullOrWhiteSpace(certificate.Purpose) ? "" : certificate.Purpose.Trim();
                bool purposeArabic = ArabicText.ContainsArabic(purpose);
                col.Item().PaddingTop(16).Text(ClosingFr(certificate.Purpose));
                if (purpose.Length > 0 && purposeArabic)
                    col.Item().PaddingTop(2).AlignRight().Text(ArabicText.FixRtlDigits(purpose));
                col.Item().PaddingTop(2).AlignRight().Text("سُلّمت هذه الشهادة للمعني(ة) بالأمر قصد استعمالها عند الحاجة.");
            }

            // ── signature block ──
            col.Item().PaddingTop(34).AlignRight().Column(sig =>
            {
                // "Fait à <ville>, le <date>" — the city is a free field that CAN be Arabic, so it gets
                // its OWN element (isolated from the Latin fragments and the date) exactly like every
                // other value; otherwise an Arabic city would be mis-ordered and the date reversed.
                sig.Item().Row(r =>
                {
                    r.RelativeItem();
                    r.AutoItem().Text("Fait à ");
                    r.AutoItem().Text(ArabicText.FixRtlDigits(City()));
                    r.AutoItem().Text(", le " + D(certificate.IssueDate));
                });
                sig.Item().AlignRight().Text(ArabicText.FixRtlDigits("حُرّر يوم : " + D(certificate.IssueDate)));
                sig.Item().PaddingTop(40).AlignRight().Text("La Direction").SemiBold();
                sig.Item().AlignRight().Text("الإدارة").SemiBold();
            });
        }

        // ── one bilingual field row : FR label + value (isolated) on the left, AR label on the right ──
        private static void Field(ColumnDescriptor col, string frLabel, string value, string arLabel)
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Row(left =>
                {
                    left.ConstantItem(150).Text(frLabel + " :").SemiBold();
                    // The value gets its OWN element: an Arabic value keeps RTL + letter-joining, a Latin
                    // value stays LTR, a date/number is never reversed. FixRtlDigits guards Arabic+digits.
                    left.RelativeItem().Text(ArabicText.FixRtlDigits(value ?? string.Empty));
                });
                row.ConstantItem(160).AlignRight().Text(arLabel);
            });
        }

        /// <summary>Renders the free body text of a « document libre », paragraph by paragraph, each in
        /// its own element (bilingual-safe): an Arabic paragraph is right-aligned and digit-corrected,
        /// a Latin one left-aligned. Never mixes a multi-word Arabic run with Latin in one element.</summary>
        /// <summary>French closing sentence. The generic legal formula is the DEFAULT reason; a
        /// user-supplied (non-Arabic) purpose REPLACES it — never appended — so the formula can never
        /// appear twice (audit IDX 49). An Arabic purpose falls back to the formula here and is printed
        /// on its own RTL line by the caller.</summary>
        public static string ClosingFr(string purpose)
        {
            string p = string.IsNullOrWhiteSpace(purpose) ? "" : purpose.Trim();
            string reason = (p.Length == 0 || ArabicText.ContainsArabic(p))
                ? "pour servir et valoir ce que de droit"
                : p;
            return "La présente attestation est délivrée à l'intéressé(e) " + reason + ".";
        }

        private static void FreeBody(ColumnDescriptor col, string body)
        {
            string text = (body ?? string.Empty).Replace("\r\n", "\n").Trim();
            if (text.Length == 0) return;

            col.Item().PaddingTop(18);
            foreach (string paragraph in text.Split('\n'))
            {
                string p = paragraph.Trim();
                if (p.Length == 0) { col.Item().PaddingTop(6); continue; }
                if (ArabicText.ContainsArabic(p))
                    col.Item().PaddingTop(4).AlignRight().Text(ArabicText.FixRtlDigits(p)).LineHeight(1.6f);
                else
                    col.Item().PaddingTop(4).Text(p).LineHeight(1.6f);
            }
        }

        // ── helpers ──
        private static string FullName(Employee e)
        {
            string ar = ((e.LastNameAr ?? "") + " " + (e.FirstNameAr ?? "")).Trim();
            if (ar.Length > 0) return ar;
            return ((e.LastNameFr ?? "") + " " + (e.FirstNameFr ?? "")).Trim();
        }

        private static string Poste(Employee e) => string.IsNullOrWhiteSpace(e.Poste) ? "—" : e.Poste;

        private static string CompanyIds(Company c)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(c.Rc)) parts.Add("RC : " + c.Rc);
            if (!string.IsNullOrWhiteSpace(c.Nif)) parts.Add("NIF : " + c.Nif);
            if (!string.IsNullOrWhiteSpace(c.CnasEmployerNumber)) parts.Add("CNAS : " + c.CnasEmployerNumber);
            return string.Join("   ", parts);
        }

        private string City()
        {
            Company c = _model.Company;
            if (c != null && !string.IsNullOrWhiteSpace(c.City)) return c.City.Trim();
            if (c != null && !string.IsNullOrWhiteSpace(c.AddressFr)) return c.AddressFr.Split(',')[0].Trim();
            return "Alger";
        }

        private string SeniorityFr()
        {
            var parts = new List<string>();
            if (_model.SeniorityYears > 0) parts.Add(_model.SeniorityYears + " an" + (_model.SeniorityYears > 1 ? "s" : ""));
            if (_model.SeniorityMonths > 0) parts.Add(_model.SeniorityMonths + " mois");
            return string.Join(" et ", parts);
        }

        private static string D(DateTime d) => d.ToString("dd/MM/yyyy", Fr);

        private static string HeadingFr(CertificateType type)
        {
            switch (type)
            {
                case CertificateType.WorkCertificate: return "ATTESTATION DE TRAVAIL";
                case CertificateType.WorkExperience: return "CERTIFICAT DE TRAVAIL";
                case CertificateType.SalaryCertificate: return "ATTESTATION DE SALAIRE";
                default: return "ATTESTATION";
            }
        }

        private static string HeadingAr(CertificateType type)
        {
            switch (type)
            {
                case CertificateType.WorkCertificate: return "شهادة عمل";
                case CertificateType.WorkExperience: return "شهادة نهاية عمل";
                case CertificateType.SalaryCertificate: return "شهادة أجر";
                default: return "شهادة";
            }
        }
    }
}
