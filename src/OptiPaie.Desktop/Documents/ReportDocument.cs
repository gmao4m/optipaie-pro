using System.Collections.Generic;
using System.Linq;
using OptiPaie.Core.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// A generic report document (A4 landscape) that renders any <see cref="ReportTable"/> —
    /// title, subtitle and a bordered table with numeric columns right-aligned. Built with
    /// the same QuestPDF engine as the payslip.
    /// </summary>
    public sealed class ReportDocument
    {
        private readonly ReportTable _table;
        private readonly HashSet<int> _numeric;

        public ReportDocument(ReportTable table)
        {
            _table = table;
            _numeric = new HashSet<int>(table.NumericColumns ?? new List<int>());
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(8.5f));

                page.Header().Column(col =>
                {
                    col.Item().Text(_table.Title ?? string.Empty).FontSize(14).SemiBold();
                    if (!string.IsNullOrWhiteSpace(_table.Subtitle))
                        col.Item().Text(_table.Subtitle).FontSize(9).FontColor("#555");
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        for (int i = 0; i < _table.Columns.Count; i++)
                        {
                            // First column a bit wider (usually a name/label).
                            c.RelativeColumn(i == 0 ? 2.4f : 1.4f);
                        }
                    });

                    table.Header(h =>
                    {
                        for (int i = 0; i < _table.Columns.Count; i++)
                        {
                            var cell = h.Cell().Element(Head);
                            (_numeric.Contains(i) ? cell.AlignRight() : cell).Text(_table.Columns[i]);
                        }
                    });

                    foreach (var row in _table.Rows)
                    {
                        for (int i = 0; i < _table.Columns.Count; i++)
                        {
                            string value = i < row.Count ? row[i] : string.Empty;
                            var cell = table.Cell().Element(Body);
                            (_numeric.Contains(i) ? cell.AlignRight() : cell).Text(value);
                        }
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

        private static IContainer Head(IContainer c) =>
            c.Background("#E8EDF2").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());

        private static IContainer Body(IContainer c) =>
            c.Border(0.5f).BorderColor("#C9D2DC").Padding(3);
    }
}
