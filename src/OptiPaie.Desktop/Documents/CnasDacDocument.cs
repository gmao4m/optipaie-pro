using System.Collections.Generic;
using System.Globalization;
using OptiPaie.Core.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// Print sheet of the DAC recap (A4 portrait) — the accountant files it or reads from it to
    /// key the CNAS portal. Built on the same QuestPDF engine as the payslip / generic report.
    /// It renders already-computed figures (CnasDacReport) — it computes nothing and changes no rate.
    /// </summary>
    public sealed class CnasDacDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly CnasDacReport _r;
        private readonly string _companyName;
        private readonly string _periodLabel;
        private readonly IReadOnlyList<CnasMovementRow> _movements;

        public CnasDacDocument(CnasDacReport report, string companyName, string periodLabel,
            IReadOnlyList<CnasMovementRow> movements)
        {
            _r = report;
            _companyName = companyName ?? string.Empty;
            _periodLabel = periodLabel ?? string.Empty;
            _movements = movements ?? new List<CnasMovementRow>();
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Sans).FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("Déclaration d'assiette de cotisation (DAC)").FontSize(15).SemiBold();
                    col.Item().Text(_companyName + "  ·  " + _periodLabel).FontSize(10).FontColor("#555");
                    string employer = string.IsNullOrEmpty(_r.CnasEmployerNumber) ? "—" : _r.CnasEmployerNumber;
                    col.Item().Text("N° employeur CNAS : " + employer + "        Effectif déclaré : " + _r.Effectif)
                        .FontSize(9).FontColor("#555");
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    // Assiette + cotisations aux taux appliqués.
                    col.Item().Element(c => SummaryTable(c));
                    col.Item().PaddingTop(6).Text(
                        "À recopier sur le portail CNAS (saisie manuelle). Le portail calcule les cotisations à partir de "
                        + "l'assiette et de l'effectif ; la ventilation ci-dessous est un contrôle interne.")
                        .FontSize(8).FontColor("#777");

                    // Répartition officielle (contrôle interne).
                    col.Item().PaddingTop(14).Text("Répartition officielle (décret 94-187 modifié) — à confirmer")
                        .SemiBold().FontSize(10);
                    col.Item().PaddingTop(4).Element(c => BranchTable(c));

                    // Annexe mouvements de la période.
                    if (_movements.Count > 0)
                    {
                        col.Item().PaddingTop(14).Text("Mouvements de la période (entrées / sorties)")
                            .SemiBold().FontSize(10);
                        col.Item().PaddingTop(4).Element(c => MovementsTable(c));
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("OptiPaie PRO — ").FontSize(8).FontColor("#999");
                    t.CurrentPageNumber().FontSize(8).FontColor("#999");
                    t.Span(" / ").FontSize(8).FontColor("#999");
                    t.TotalPages().FontSize(8).FontColor("#999");
                });
            });
        }

        private void SummaryTable(IContainer container)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(3f); c.RelativeColumn(2f); });
                Row(table, "Assiette cotisable", Money(_r.Assiette), strong: true);
                Row(table, "Cotisation salariale (" + Pct(_r.RateSalariale) + ")", Money(_r.CotisationSalariale));
                Row(table, "Cotisation patronale (" + Pct(_r.RatePatronale) + ")", Money(_r.CotisationPatronale));
                Row(table, "Total des cotisations", Money(_r.CotisationTotale), strong: true);
            });
        }

        private static void Row(TableDescriptor table, string label, string value, bool strong = false)
        {
            var l = table.Cell().Element(Body).Text(label);
            var v = table.Cell().Element(Body).AlignRight().Text(value);
            if (strong) { l.SemiBold(); v.SemiBold(); }
        }

        private void BranchTable(IContainer container)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2.4f); c.RelativeColumn(1.2f); c.RelativeColumn(1.6f);
                    c.RelativeColumn(1.2f); c.RelativeColumn(1.6f);
                });
                table.Header(h =>
                {
                    h.Cell().Element(Head).Text("Branche");
                    h.Cell().Element(Head).AlignRight().Text("Taux patronal");
                    h.Cell().Element(Head).AlignRight().Text("Montant patronal");
                    h.Cell().Element(Head).AlignRight().Text("Taux salarial");
                    h.Cell().Element(Head).AlignRight().Text("Montant salarial");
                });
                foreach (CnasContributionBranch b in _r.OfficialBranches)
                {
                    table.Cell().Element(Body).Text(b.Name);
                    table.Cell().Element(Body).AlignRight().Text(Pct(b.PatronaleRate));
                    table.Cell().Element(Body).AlignRight().Text(Money(b.PatronaleAmount));
                    table.Cell().Element(Body).AlignRight().Text(b.SalarialeRate > 0 ? Pct(b.SalarialeRate) : "—");
                    table.Cell().Element(Body).AlignRight().Text(b.SalarialeRate > 0 ? Money(b.SalarialeAmount) : "—");
                }
            });
        }

        private void MovementsTable(IContainer container)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.2f); c.RelativeColumn(2.6f); c.RelativeColumn(2f); c.RelativeColumn(1.6f);
                });
                table.Header(h =>
                {
                    h.Cell().Element(Head).Text("Type");
                    h.Cell().Element(Head).Text("Salarié");
                    h.Cell().Element(Head).Text("N° sécurité sociale");
                    h.Cell().Element(Head).Text("Date");
                });
                foreach (CnasMovementRow m in _movements)
                {
                    table.Cell().Element(Body).Text(m.IsEntry ? "Entrée" : "Sortie");
                    table.Cell().Element(Body).Text((m.LastName + " " + m.FirstName).Trim());
                    table.Cell().Element(Body).Text(string.IsNullOrEmpty(m.Nss) ? "—" : m.Nss);
                    table.Cell().Element(Body).Text(m.Date.ToString("dd/MM/yyyy", Fr));
                }
            });
        }

        private static string Money(decimal v) => v.ToString("N2", Fr) + " DA";
        private static string Pct(decimal rate) => (rate * 100m).ToString("0.###", Fr) + " %";

        private static IContainer Head(IContainer c) =>
            c.Background("#E8EDF2").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());

        private static IContainer Body(IContainer c) =>
            c.Border(0.5f).BorderColor("#C9D2DC").Padding(4);
    }
}
