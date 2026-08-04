using System.Collections.Generic;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Flat, pre-formatted data for a performance report PDF (already localised).</summary>
    public sealed class PerformanceReportModel
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string CompanyName { get; set; }
        public string AverageText { get; set; }
        public string BestText { get; set; }
        public string[] Columns { get; set; } = new string[0];
        public List<string[]> Rows { get; set; } = new List<string[]>();
    }

    /// <summary>A performance/evaluation report PDF (A4). Renders the already-aggregated model;
    /// it computes nothing.</summary>
    public sealed class PerformanceReportDocument
    {
        private const string Navy = "#0E3B2C";
        private const string Teal = "#057A55";
        private const string Ink = "#182B26";
        private const string Muted = "#5B6B66";
        private const string HeadFill = "#F2F1EC";
        private const string Line = "#D9DCE1";

        private readonly PerformanceReportModel _m;

        public PerformanceReportDocument(PerformanceReportModel model) { _m = model; }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Sans).FontSize(9).FontColor(Ink));

                page.Header().Column(head =>
                {
                    if (!string.IsNullOrEmpty(_m.CompanyName))
                        head.Item().Text(_m.CompanyName).FontColor(Muted).FontSize(10);
                    head.Item().Text(_m.Title ?? string.Empty).FontColor(Navy).FontSize(18).SemiBold();
                    if (!string.IsNullOrEmpty(_m.Subtitle))
                        head.Item().PaddingTop(2).Text(_m.Subtitle).FontColor(Muted);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);

                    if (!string.IsNullOrEmpty(_m.AverageText) || !string.IsNullOrEmpty(_m.BestText))
                    {
                        col.Item().Row(row =>
                        {
                            if (!string.IsNullOrEmpty(_m.AverageText))
                                row.RelativeItem().Background(HeadFill).Padding(8)
                                    .Text(text => { text.Span("Moyenne : ").FontColor(Muted); text.Span(_m.AverageText).SemiBold().FontColor(Teal); });
                            if (!string.IsNullOrEmpty(_m.BestText))
                                row.RelativeItem().PaddingLeft(8).Background(HeadFill).Padding(8)
                                    .Text(_m.BestText).FontColor(Ink);
                        });
                    }

                    if (_m.Columns.Length > 0)
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                for (int i = 0; i < _m.Columns.Length; i++)
                                {
                                    if (i == 1) columns.RelativeColumn(3);
                                    else columns.RelativeColumn();
                                }
                            });
                            table.Header(header =>
                            {
                                foreach (string c in _m.Columns)
                                    header.Cell().Background(Navy).Padding(6).Text(text => text.Span(c ?? string.Empty).FontColor("#FFFFFF").SemiBold());
                            });
                            foreach (string[] r in _m.Rows)
                                foreach (string cell in r)
                                    table.Cell().BorderBottom(0.5f).BorderColor(Line).Padding(6).Text(cell ?? string.Empty);
                        });
                    }
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.Span("OptiPaie PRO — ").FontColor(Muted).FontSize(8);
                    text.CurrentPageNumber().FontColor(Muted).FontSize(8);
                    text.Span(" / ").FontColor(Muted).FontSize(8);
                    text.TotalPages().FontColor(Muted).FontSize(8);
                });
            });
        }
    }
}
