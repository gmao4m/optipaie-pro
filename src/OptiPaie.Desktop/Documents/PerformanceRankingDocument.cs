using System.Collections.Generic;
using System.Globalization;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Data for the company-wide performance synthesis (department summary + ranking).</summary>
    public sealed class PerformanceRankingModel
    {
        public Company Company { get; set; }
        public string ScopeLabel { get; set; }
        public PerformanceDashboard Dashboard { get; set; }
    }

    /// <summary>
    /// Company-wide evaluation synthesis (A4): the company average, a per-department summary,
    /// and the ranking (best elements + those to support). Same QuestPDF engine as the payslip
    /// and the individual review sheet.
    /// </summary>
    public sealed class PerformanceRankingDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly PerformanceRankingModel _model;

        public PerformanceRankingDocument(PerformanceRankingModel model)
        {
            _model = model;
        }

        public void Compose(IDocumentContainer container)
        {
            Company company = _model.Company;
            PerformanceDashboard d = _model.Dashboard;

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(company?.NameFr ?? string.Empty).FontSize(14).SemiBold();
                    col.Item().PaddingTop(6).AlignCenter().Text("SYNTHÈSE DES ÉVALUATIONS").FontSize(15).SemiBold();
                    if (!string.IsNullOrWhiteSpace(_model.ScopeLabel))
                    {
                        col.Item().AlignCenter().Text(_model.ScopeLabel).FontSize(10).FontColor("#666");
                    }
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Background("#F2F5F8").Padding(8).Text(t =>
                    {
                        t.Span("Moyenne entreprise : ").SemiBold();
                        t.Span(Pct(d.CompanyAveragePercent)).FontSize(13).SemiBold();
                        t.Span("     Évaluations finalisées : ").SemiBold();
                        t.Span(d.ReviewCount.ToString(Fr));
                    });

                    // --- department summary ---
                    col.Item().PaddingTop(10).Text("Synthèse par département").SemiBold().FontSize(11);
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(1.5f); c.RelativeColumn(1.5f); });
                        table.Header(h =>
                        {
                            h.Cell().Element(Head).Text("Département");
                            h.Cell().Element(Head).AlignCenter().Text("Évaluations");
                            h.Cell().Element(Head).AlignCenter().Text("Moyenne");
                        });
                        foreach (DeptScoreRow r in d.DepartmentAverages)
                        {
                            table.Cell().Element(Body).Text(string.IsNullOrWhiteSpace(r.Department) ? "—" : r.Department);
                            table.Cell().Element(Body).AlignCenter().Text(r.ReviewCount.ToString(Fr));
                            table.Cell().Element(Body).AlignCenter().Text(Pct(r.AveragePercent));
                        }
                    });

                    // --- ranking: best ---
                    if (d.TopPerformers.Count > 0)
                    {
                        col.Item().PaddingTop(12).Text("Classement — meilleurs éléments").SemiBold().FontSize(11);
                        col.Item().PaddingTop(4).Element(c => RankingTable(c, d.TopPerformers));
                    }

                    // --- ranking: to support ---
                    if (d.BottomPerformers.Count > 0)
                    {
                        col.Item().PaddingTop(12).Text("Collaborateurs à accompagner").SemiBold().FontSize(11);
                        col.Item().PaddingTop(4).Element(c => RankingTable(c, d.BottomPerformers));
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("OptiPaie PRO — ").FontSize(8).FontColor("#999");
                    t.Span(company?.NameFr ?? string.Empty).FontSize(8).FontColor("#999");
                });
            });
        }

        private static void RankingTable(IContainer container, IReadOnlyList<PerformerRow> rows)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.1f);
                    c.RelativeColumn(1.6f);
                });
                table.Header(h =>
                {
                    h.Cell().Element(Head).AlignCenter().Text("#");
                    h.Cell().Element(Head).Text("Employé");
                    h.Cell().Element(Head).Text("Département");
                    h.Cell().Element(Head).AlignCenter().Text("Note");
                    h.Cell().Element(Head).AlignCenter().Text("%");
                    h.Cell().Element(Head).Text("Appréciation");
                });

                int rank = 1;
                foreach (PerformerRow r in rows)
                {
                    string note = r.LatestScore.ToString("0.##", Fr) + " / " + (r.ScaleMax <= 0m ? 20m : r.ScaleMax).ToString("0.##", Fr);
                    table.Cell().Element(Body).AlignCenter().Text((rank++).ToString(Fr));
                    table.Cell().Element(Body).Text(r.EmployeeName ?? string.Empty);
                    table.Cell().Element(Body).Text(string.IsNullOrWhiteSpace(r.Department) ? "—" : r.Department);
                    table.Cell().Element(Body).AlignCenter().Text(note);
                    table.Cell().Element(Body).AlignCenter().Text(Pct(r.ScorePercent));
                    table.Cell().Element(Body).Text(r.Rating ?? string.Empty);
                }
            });
        }

        private static string Pct(decimal p) => p.ToString("0.#", Fr) + " %";

        private static IContainer Head(IContainer c) =>
            c.Background("#E8EDF2").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());

        private static IContainer Body(IContainer c) =>
            c.Border(0.5f).BorderColor("#C9D2DC").Padding(4);
    }
}
