using System;
using System.Globalization;
using OptiPaie.Core.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Services.Documents
{
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
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(_m.CompanyName ?? string.Empty).FontSize(14).SemiBold();
                    col.Item().PaddingTop(6).AlignCenter().Text("قرار عطلة — Décision de congé").FontSize(16).Bold();
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(t => { t.Span("Employé / الموظف : ").SemiBold(); t.Span(_m.EmployeeName ?? string.Empty); });
                    col.Item().Text(t => { t.Span("Type / النوع : ").SemiBold(); t.Span(_m.TypeLabel ?? string.Empty); });
                    col.Item().Text(t => { t.Span("Paiement / الأجر : ").SemiBold(); t.Span(_m.PaymentLabel ?? string.Empty); });
                    col.Item().Text(t => { t.Span("Période / الفترة : ").SemiBold(); t.Span(D(_m.StartDate) + "  →  " + D(_m.EndDate)); });
                    col.Item().Text(t => { t.Span("Jours décomptés / عدد الأيام : ").SemiBold(); t.Span(Num(_m.Days)); });
                    col.Item().PaddingTop(30).Text("Décision : ACCORDÉE — القرار: مقبول").SemiBold();
                    col.Item().PaddingTop(40).AlignRight().Text("Fait le " + D(_m.DecisionDate));
                    col.Item().PaddingTop(6).AlignRight().Text("Signature et cachet / التوقيع والختم");
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
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(_m.CompanyName ?? string.Empty).FontSize(14).SemiBold();
                    col.Item().PaddingTop(6).AlignCenter().Text("شهادة رصيد العطل — Attestation de solde de congé").FontSize(15).Bold();
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(t => { t.Span("Employé / الموظف : ").SemiBold(); t.Span(_m.EmployeeName ?? string.Empty); });
                    col.Item().Text(t => { t.Span("Année / السنة : ").SemiBold(); t.Span(_m.Year.ToString(CultureInfo.InvariantCulture)); });
                    col.Item().PaddingTop(10).Text(t => { t.Span("Droit acquis / المكتسب : ").SemiBold(); t.Span(Num(_m.Entitlement) + " jours"); });
                    col.Item().Text(t => { t.Span("Consommé / المستهلك : ").SemiBold(); t.Span(Num(_m.Taken) + " jours"); });
                    col.Item().Text(t => { t.Span("Réservé / المحجوز : ").SemiBold(); t.Span(Num(_m.Pending) + " jours"); });
                    col.Item().Text(t => { t.Span("Disponible / المتاح : ").SemiBold().FontSize(13); t.Span(Num(_m.Available) + " jours").FontSize(13); });
                    col.Item().PaddingTop(40).AlignRight().Text("Signature et cachet / التوقيع والختم");
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
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(_companyName ?? string.Empty).FontSize(14).SemiBold();
                    col.Item().PaddingTop(6).AlignCenter().Text("تصفية رصيد العطل — Reliquat de congé (solde de tout compte)").FontSize(14).Bold();
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(t => { t.Span("Employé / الموظف : ").SemiBold(); t.Span(_s.EmployeeName ?? string.Empty); });
                    col.Item().Text(t => { t.Span("Date de sortie / تاريخ المغادرة : ").SemiBold(); t.Span(D(_s.ExitDate)); });
                    col.Item().PaddingTop(10).Text(t => { t.Span("Droit acquis (prorata) / المكتسب : ").SemiBold(); t.Span(Num(_s.Acquired) + " jours"); });
                    col.Item().Text(t => { t.Span("Déjà pris / المستهلك : ").SemiBold(); t.Span(Num(_s.Taken) + " jours"); });
                    col.Item().Text(t => { t.Span("Jours dus / الأيام المستحقة : ").SemiBold(); t.Span(Num(_s.RemainingDays) + " jours"); });
                    col.Item().Text(t => { t.Span("Salaire mensuel / الأجر الشهري : ").SemiBold(); t.Span(Num(_s.MonthlySalary)); });
                    col.Item().Text(t => { t.Span("Taux journalier / الأجر اليومي : ").SemiBold(); t.Span(Num(_s.DailyRate)); });
                    col.Item().PaddingTop(8).Text(t => { t.Span("Montant du reliquat / مبلغ التصفية : ").SemiBold().FontSize(14); t.Span(Num(_s.Amount)).Bold().FontSize(14); });
                    col.Item().PaddingTop(40).AlignRight().Text("Signature et cachet / التوقيع والختم");
                });

                page.Footer().AlignCenter().Text("OptiPaie PRO");
            });
        }

        private static string D(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        private static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
