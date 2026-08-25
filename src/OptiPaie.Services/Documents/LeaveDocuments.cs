using System;
using System.Globalization;
using OptiPaie.Core.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Services.Documents
{
    /// <summary>
    /// Shared rendering helpers for the bilingual (French + Arabic) leave documents.
    ///
    /// QuestPDF 2022.12 has no real Unicode bidi: it picks ONE base direction per paragraph
    /// (from the first strong character) and reverses the other-direction runs. A paragraph that
    /// mixes French and a multi-word Arabic run therefore comes out with one side reversed.
    /// The rule here is simple and robust: never mix a multi-word Arabic run with Latin in the
    /// SAME text. Titles are split into a French line + a pure-Arabic line, and every field value
    /// (which may itself be an Arabic name) is a SEPARATE text element that gets its own base
    /// direction. The label prefix keeps a single Arabic word ("الموظف"), which renders correctly.
    ///
    /// The font is the bundled IBM Plex Sans Arabic (registered at app start) — it carries BOTH
    /// Latin and Arabic glyphs and is embedded in the PDF, so nothing depends on the client's
    /// installed fonts and no glyph ever falls back to "□"/"????".
    /// </summary>
    internal static class LeaveDoc
    {
        public const string Font = "IBM Plex Sans Arabic";

        /// <summary>A centred bilingual title as two stacked, single-language lines.</summary>
        public static void Title(ColumnDescriptor col, string fr, string ar, float size)
        {
            col.Item().PaddingTop(6).AlignCenter().Text(fr).FontSize(size).Bold();
            col.Item().AlignCenter().Text(ar).FontSize(size).Bold();
        }

        /// <summary>One info line: "label :" (French + single Arabic word) then the value in its
        /// OWN text element so an Arabic value keeps its correct right-to-left order.</summary>
        public static void Line(ColumnDescriptor col, string label, string value, float valueSize = 0f)
        {
            col.Item().Row(row =>
            {
                row.AutoItem().Text(label).SemiBold();
                var span = row.RelativeItem().PaddingLeft(6).Text(value ?? string.Empty);
                if (valueSize > 0f) span.FontSize(valueSize);
            });
        }

        /// <summary>A right-aligned pair of single-language lines (French then Arabic).</summary>
        public static void RightPair(ColumnDescriptor col, string fr, string ar)
        {
            col.Item().AlignRight().Text(fr);
            col.Item().AlignRight().Text(ar);
        }
    }

    /// <summary>Data for a leave decision (قرار عطلة).</summary>
    public sealed class LeaveDecisionModel
    {
        public string CompanyName { get; set; }
        public string EmployeeName { get; set; }
        public string TypeLabel { get; set; }
        public string PaymentLabel { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Days { get; set; }
        public DateTime DecisionDate { get; set; }
    }

    /// <summary>Décision de congé — قرار عطلة (A4).</summary>
    public sealed class LeaveDecisionDocument
    {
        private readonly LeaveDecisionModel _m;
        public LeaveDecisionDocument(LeaveDecisionModel model) { _m = model; }

        public void Compose(IDocumentContainer container)
        {
            DocumentFonts.EnsureRegistered();
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontFamily(LeaveDoc.Font).FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(_m.CompanyName ?? string.Empty).FontSize(14).SemiBold();
                    LeaveDoc.Title(col, "Décision de congé", "قرار عطلة", 16);
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Spacing(10);
                    LeaveDoc.Line(col, "Employé / الموظف :", _m.EmployeeName);
                    LeaveDoc.Line(col, "Type / النوع :", _m.TypeLabel);
                    LeaveDoc.Line(col, "Paiement / الأجر :", _m.PaymentLabel);
                    LeaveDoc.Line(col, "Période / الفترة :", D(_m.StartDate) + "  →  " + D(_m.EndDate));
                    LeaveDoc.Line(col, "Jours décomptés / عدد الأيام :", Num(_m.Days));
                    col.Item().PaddingTop(30).Text("Décision : ACCORDÉE").SemiBold();
                    col.Item().Text("القرار : مقبول").SemiBold();
                    col.Item().PaddingTop(40).AlignRight().Text("Fait le " + D(_m.DecisionDate));
                    LeaveDoc.RightPair(col, "Signature et cachet", "التوقيع والختم");
                });

                page.Footer().AlignCenter().Text("OptiPaie PRO");
            });
        }

        private static string D(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        private static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Data for a leave-balance certificate (شهادة رصيد العطل).</summary>
    public sealed class LeaveBalanceCertificateModel
    {
        public string CompanyName { get; set; }
        public string EmployeeName { get; set; }
        public int Year { get; set; }
        public decimal Entitlement { get; set; }
        public decimal Taken { get; set; }
        public decimal Pending { get; set; }
        public decimal Available { get; set; }
    }

    /// <summary>Attestation de solde de congé — شهادة رصيد العطل (A4).</summary>
    public sealed class LeaveBalanceCertificateDocument
    {
        private readonly LeaveBalanceCertificateModel _m;
        public LeaveBalanceCertificateDocument(LeaveBalanceCertificateModel model) { _m = model; }

        public void Compose(IDocumentContainer container)
        {
            DocumentFonts.EnsureRegistered();
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontFamily(LeaveDoc.Font).FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(_m.CompanyName ?? string.Empty).FontSize(14).SemiBold();
                    LeaveDoc.Title(col, "Attestation de solde de congé", "شهادة رصيد العطل", 15);
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Spacing(10);
                    LeaveDoc.Line(col, "Employé / الموظف :", _m.EmployeeName);
                    LeaveDoc.Line(col, "Année / السنة :", _m.Year.ToString(CultureInfo.InvariantCulture));
                    col.Item().PaddingTop(10);
                    LeaveDoc.Line(col, "Droit acquis / المكتسب :", Num(_m.Entitlement) + " jours");
                    LeaveDoc.Line(col, "Consommé / المستهلك :", Num(_m.Taken) + " jours");
                    LeaveDoc.Line(col, "Réservé / المحجوز :", Num(_m.Pending) + " jours");
                    LeaveDoc.Line(col, "Disponible / المتاح :", Num(_m.Available) + " jours", 13f);
                    col.Item().PaddingTop(40);
                    LeaveDoc.RightPair(col, "Signature et cachet", "التوقيع والختم");
                });

                page.Footer().AlignCenter().Text("OptiPaie PRO");
            });
        }

        private static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Reliquat de congé (solde de tout compte) — تصفية رصيد العطل (A4).</summary>
    public sealed class LeaveSettlementDocument
    {
        private readonly FinalSettlement _s;
        private readonly string _companyName;
        public LeaveSettlementDocument(FinalSettlement settlement, string companyName) { _s = settlement; _companyName = companyName; }

        public void Compose(IDocumentContainer container)
        {
            DocumentFonts.EnsureRegistered();
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontFamily(LeaveDoc.Font).FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(_companyName ?? string.Empty).FontSize(14).SemiBold();
                    LeaveDoc.Title(col, "Reliquat de congé (solde de tout compte)", "تصفية رصيد العطل", 14);
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Spacing(10);
                    LeaveDoc.Line(col, "Employé / الموظف :", _s.EmployeeName);
                    LeaveDoc.Line(col, "Date de sortie / تاريخ المغادرة :", D(_s.ExitDate));
                    col.Item().PaddingTop(10);
                    LeaveDoc.Line(col, "Droit acquis (prorata) / المكتسب :", Num(_s.Acquired) + " jours");
                    LeaveDoc.Line(col, "Déjà pris / المستهلك :", Num(_s.Taken) + " jours");
                    LeaveDoc.Line(col, "Jours dus / الأيام المستحقة :", Num(_s.RemainingDays) + " jours");
                    LeaveDoc.Line(col, "Salaire mensuel / الأجر الشهري :", Num(_s.MonthlySalary));
                    LeaveDoc.Line(col, "Taux journalier / الأجر اليومي :", Num(_s.DailyRate));
                    col.Item().PaddingTop(8);
                    LeaveDoc.Line(col, "Montant du reliquat / مبلغ التصفية :", Num(_s.Amount), 14f);
                    col.Item().PaddingTop(40);
                    LeaveDoc.RightPair(col, "Signature et cachet", "التوقيع والختم");
                });

                page.Footer().AlignCenter().Text("OptiPaie PRO");
            });
        }

        private static string D(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        private static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
