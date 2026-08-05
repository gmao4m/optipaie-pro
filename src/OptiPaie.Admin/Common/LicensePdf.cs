using System.Collections.Generic;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Admin.Common
{
    /// <summary>One printable row of the license batch PDF.</summary>
    public sealed class LicenseBatchRow
    {
        public int Index { get; set; }
        public string Key { get; set; }
        public string Type { get; set; }      // Mono-société / Multi-sociétés
        public string Duration { get; set; }  // À vie / Annuelle
    }

    /// <summary>
    /// Renders a professional, printable PDF of a generated license batch in the
    /// OptiPaie identity (emerald + gold). Self-contained (QuestPDF 2022.12.1, no
    /// license key needed at this version).
    /// </summary>
    public static class LicensePdf
    {
        private const string Emerald = "#0F6E56";
        private const string EmeraldSoft = "#E1F5EE";
        private const string Gold = "#E3B341";
        private const string Ink = "#1B2430";
        private const string Muted = "#8A94A2";
        private const string RowAlt = "#F7F9FB";

        public static void Save(string path, IReadOnlyList<LicenseBatchRow> rows, string note, string generatedOn)
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink).FontFamily("Segoe UI"));

                    page.Header().Element(h => Header(h, rows.Count, generatedOn, note));
                    page.Content().PaddingVertical(14).Element(c => Table(c, rows));
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontColor(Muted).FontSize(9));
                        t.Span("OptiPaie PRO — Gestionnaire de licences   •   page ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf(path);
        }

        private static void Header(IContainer container, int count, string on, string note)
        {
            container.Column(col =>
            {
                col.Item().Background(Emerald).Padding(16).Row(row =>
                {
                    row.RelativeItem().Column(x =>
                    {
                        x.Item().Text("OptiPaie PRO").FontSize(20).Bold().FontColor("#FFFFFF");
                        x.Item().PaddingTop(2).Text("Licences générées").FontSize(11).FontColor(EmeraldSoft);
                    });
                    row.ConstantItem(150).AlignRight().AlignMiddle()
                        .Text($"{count} licence(s)").FontSize(13).Bold().FontColor(Gold);
                });
                col.Item().Height(3).Background(Gold);
                col.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Text($"Généré le {on}").FontColor("#4E5A69");
                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        row.RelativeItem().AlignRight().Text($"Lot : {note}").FontColor("#4E5A69");
                    }
                });
            });
        }

        private static void Table(IContainer container, IReadOnlyList<LicenseBatchRow> rows)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(34);
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "#");
                    HeaderCell(header.Cell(), "Clé de licence");
                    HeaderCell(header.Cell(), "Type");
                    HeaderCell(header.Cell(), "Durée");
                });

                bool alt = false;
                foreach (LicenseBatchRow r in rows)
                {
                    string bg = alt ? RowAlt : "#FFFFFF";
                    alt = !alt;
                    Cell(table.Cell(), bg).Text((r.Index).ToString()).FontColor(Muted);
                    Cell(table.Cell(), bg).Text(r.Key).FontFamily("Consolas").FontSize(11).Bold().FontColor(Emerald);
                    Cell(table.Cell(), bg).Text(r.Type ?? string.Empty);
                    Cell(table.Cell(), bg).Text(r.Duration ?? string.Empty);
                }
            });
        }

        private static void HeaderCell(IContainer cell, string text) =>
            cell.Background(Emerald).PaddingVertical(7).PaddingHorizontal(8).Text(text).Bold().FontColor("#FFFFFF");

        private static IContainer Cell(IContainer cell, string background) =>
            cell.Background(background).BorderBottom(1).BorderColor("#E7EBF0").PaddingVertical(6).PaddingHorizontal(8);
    }
}
