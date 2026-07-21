using System.Globalization;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Data for the performance-review document.</summary>
    public sealed class PerformanceReviewModel
    {
        public Company Company { get; set; }
        public Employee Employee { get; set; }
        public PerformanceDetail Detail { get; set; }
    }

    /// <summary>Performance-review sheet (A4), built with the same QuestPDF engine as the payslip.</summary>
    public sealed class PerformanceReviewDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly PerformanceReviewModel _model;

        public PerformanceReviewDocument(PerformanceReviewModel model)
        {
            _model = model;
        }

        public void Compose(IDocumentContainer container)
        {
            Company company = _model.Company;
            Employee employee = _model.Employee;
            PerformanceDetail detail = _model.Detail;
            PerformanceReview review = detail.Review;

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(company?.NameFr ?? string.Empty).FontSize(14).SemiBold();
                    col.Item().PaddingTop(6).AlignCenter().Text("FICHE D'ÉVALUATION").FontSize(15).SemiBold();
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Text(t =>
                    {
                        t.Span("Employé : ").SemiBold();
                        t.Span((employee?.LastNameFr + " " + employee?.FirstNameFr).Trim());
                        t.Span("     Période : ").SemiBold();
                        t.Span(review.PeriodLabel ?? review.PeriodYear.ToString());
                    });
                    col.Item().Text(t =>
                    {
                        t.Span("Évaluateur : ").SemiBold();
                        t.Span(review.Reviewer ?? "—");
                        t.Span("     Date : ").SemiBold();
                        t.Span(review.ReviewDate.ToString("dd/MM/yyyy", Fr));
                    });

                    if (detail.Attendance != null)
                    {
                        AttendanceContext a = detail.Attendance;
                        col.Item().PaddingTop(4).Background("#F2F5F8").Padding(6).Text(
                            "Présence : " + a.AbsentDays + " absence(s) · " + a.LateCount + " retard(s) · " +
                            a.OvertimeHours.ToString("0.##", Fr) + " h supplémentaires").FontSize(9).FontColor("#444");
                    }

                    col.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4);
                            c.RelativeColumn(1.2f);
                            c.RelativeColumn(1.2f);
                            c.RelativeColumn(3);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Element(Head).Text("Critère");
                            h.Cell().Element(Head).AlignCenter().Text("Poids");
                            h.Cell().Element(Head).AlignCenter().Text("Note /20");
                            h.Cell().Element(Head).Text("Commentaire");
                        });

                        foreach (PerformanceCriterion c in detail.Criteria)
                        {
                            table.Cell().Element(Body).Text(c.Label ?? string.Empty);
                            table.Cell().Element(Body).AlignCenter().Text(c.Weight.ToString("0.##", Fr));
                            table.Cell().Element(Body).AlignCenter().Text(c.Score.ToString("0.##", Fr));
                            table.Cell().Element(Body).Text(c.Comment ?? string.Empty);
                        }
                    });

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text(t =>
                        {
                            t.Span("Note globale : ").SemiBold();
                            t.Span(review.OverallScore.ToString("0.##", Fr) + " / 20").FontSize(13).SemiBold();
                        });
                        row.RelativeItem().AlignRight().Text(t =>
                        {
                            t.Span("Appréciation : ").SemiBold();
                            t.Span(detail.Rating ?? string.Empty).SemiBold();
                        });
                    });

                    if (!string.IsNullOrWhiteSpace(review.Comments))
                    {
                        col.Item().PaddingTop(10).Text("Observations").SemiBold();
                        col.Item().Text(review.Comments);
                    }

                    col.Item().PaddingTop(30).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("L'évaluateur").SemiBold();
                            c.Item().PaddingTop(36).Text("_______________________");
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("L'employé").SemiBold();
                            c.Item().PaddingTop(36).AlignRight().Text("_______________________");
                        });
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("OptiPaie PRO — ").FontSize(8).FontColor("#999");
                    t.Span(company?.NameFr ?? string.Empty).FontSize(8).FontColor("#999");
                });
            });
        }

        private static IContainer Head(IContainer c) =>
            c.Background("#E8EDF2").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());

        private static IContainer Body(IContainer c) =>
            c.Border(0.5f).BorderColor("#C9D2DC").Padding(4);
    }
}
