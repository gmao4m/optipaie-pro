using System.Collections.Generic;
using System.Globalization;
using OptiPaie.Core.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Data for the monthly attendance report.</summary>
    public sealed class AttendanceReportModel
    {
        public string CompanyName { get; set; }
        public string PeriodLabel { get; set; }
        public List<AttendanceSummary> Rows { get; set; } = new List<AttendanceSummary>();
    }

    /// <summary>
    /// Monthly attendance report (A4, print-identical) built with QuestPDF — the same
    /// fixed-layout engine used for the payslip.
    /// </summary>
    public sealed class AttendanceReportDocument
    {
        private readonly AttendanceReportModel _model;

        public AttendanceReportDocument(AttendanceReportModel model)
        {
            _model = model;
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(_model.CompanyName ?? string.Empty).FontSize(14).SemiBold();
                    col.Item().Text("État de présence — " + _model.PeriodLabel).FontSize(10);
                });

                page.Content().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(4);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.6f);
                        c.RelativeColumn(1.6f);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Element(Head).Text("Employé");
                        h.Cell().Element(Head).AlignCenter().Text("Présents");
                        h.Cell().Element(Head).AlignCenter().Text("Absents");
                        h.Cell().Element(Head).AlignCenter().Text("Congés");
                        h.Cell().Element(Head).AlignCenter().Text("Retards");
                        h.Cell().Element(Head).AlignRight().Text("Heures");
                        h.Cell().Element(Head).AlignRight().Text("H. supp.");
                    });

                    foreach (AttendanceSummary r in _model.Rows)
                    {
                        table.Cell().Element(Body).Text(r.EmployeeName ?? string.Empty);
                        table.Cell().Element(Body).AlignCenter().Text(r.PresentDays.ToString());
                        table.Cell().Element(Body).AlignCenter().Text(r.AbsentDays.ToString());
                        table.Cell().Element(Body).AlignCenter().Text(r.LeaveDays.ToString());
                        table.Cell().Element(Body).AlignCenter().Text(r.LateCount.ToString());
                        table.Cell().Element(Body).AlignRight().Text(Num(r.WorkedHours));
                        table.Cell().Element(Body).AlignRight().Text(Num(r.OvertimeHours));
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }

        private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static IContainer Head(IContainer c) =>
            c.Background("#E8EDF2").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());

        private static IContainer Body(IContainer c) =>
            c.Border(0.5f).BorderColor("#C9D2DC").Padding(4);
    }
}
