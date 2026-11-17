using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    // ---- shared models ----------------------------------------------------

    public sealed class AttendanceSheetModel
    {
        public string CompanyName { get; set; }
        public string PeriodLabel { get; set; }
        public int Days { get; set; }
        public List<AttendanceSheetRow> Rows { get; set; } = new List<AttendanceSheetRow>();
        public List<(string Letter, string Label)> Legend { get; set; } = new List<(string, string)>();
    }

    public sealed class AttendanceSheetRow
    {
        public string Name { get; set; }
        public string[] Day { get; set; } // Day[0] = day 1 …
    }

    public sealed class AttendanceSummaryModel
    {
        public string CompanyName { get; set; }
        public string PeriodLabel { get; set; }
        public List<AttendanceSummaryRow> Rows { get; set; } = new List<AttendanceSummaryRow>();
        public AttendanceSummaryRow Totals { get; set; } = new AttendanceSummaryRow();
    }

    public sealed class AttendanceSummaryRow
    {
        public string Name { get; set; }
        public decimal DaysDue { get; set; }
        public decimal Worked { get; set; }
        public decimal Absent { get; set; }
        public decimal Leave { get; set; }
        public decimal Half { get; set; }
        public int Late { get; set; }
        public decimal Deduction { get; set; }
    }

    // ---- 1 · company monthly attendance SHEET (employees × days) ----------

    /// <summary>The month sheet a company files and posts: employees down, days across, one letter per
    /// day, with a legend. Landscape A4; QuestPDF paginates (~one page per 25 employees).</summary>
    public sealed class AttendanceSheetDocument
    {
        private readonly AttendanceSheetModel _m;
        public AttendanceSheetDocument(AttendanceSheetModel m) { _m = m; }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontSize(7.5f));

                page.Header().Column(col =>
                {
                    col.Item().Text(_m.CompanyName ?? string.Empty).FontSize(13).SemiBold();
                    col.Item().Text("Feuille de présence   ·   " + _m.PeriodLabel).FontSize(9);
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(5);
                            for (int d = 0; d < _m.Days; d++) c.RelativeColumn(1);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Element(Head).Text("Employé");
                            for (int d = 1; d <= _m.Days; d++) h.Cell().Element(Head).AlignCenter().Text(d.ToString());
                        });

                        foreach (AttendanceSheetRow r in _m.Rows)
                        {
                            table.Cell().Element(Body).Text(r.Name ?? string.Empty);
                            for (int d = 0; d < _m.Days; d++)
                                table.Cell().Element(Body).AlignCenter().Text(d < r.Day.Length ? r.Day[d] : string.Empty);
                        }
                    });

                    col.Item().PaddingTop(8).Text(t =>
                    {
                        t.Span("Légende :  ").SemiBold();
                        t.Span(string.Join("    ", _m.Legend.Select(l => l.Letter + " = " + l.Label)));
                    });
                });

                page.Footer().AlignCenter().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
            });
        }

        private static IContainer Head(IContainer c) =>
            c.Background("#E8EDF2").Border(0.4f).BorderColor("#8A94A2").Padding(2).DefaultTextStyle(t => t.SemiBold());
        private static IContainer Body(IContainer c) => c.Border(0.4f).BorderColor("#C9D2DC").Padding(2);
    }

    // ---- 2 · per-employee monthly SUMMARY ---------------------------------

    /// <summary>Per-employee monthly summary: days due, worked, absent, on leave, half days, late, and
    /// the resulting deduction — the same figures payroll uses.</summary>
    public sealed class AttendanceSummaryDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private readonly AttendanceSummaryModel _m;
        public AttendanceSummaryDocument(AttendanceSummaryModel m) { _m = m; }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(_m.CompanyName ?? string.Empty).FontSize(14).SemiBold();
                    col.Item().Text("Récapitulatif de présence   ·   " + _m.PeriodLabel).FontSize(10);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3.4f); c.RelativeColumn(1.2f); c.RelativeColumn(1.2f); c.RelativeColumn(1.2f);
                        c.RelativeColumn(1.2f); c.RelativeColumn(1.2f); c.RelativeColumn(1.2f); c.RelativeColumn(1.6f);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Element(Head).Text("Employé");
                        h.Cell().Element(Head).AlignCenter().Text("Jours dus");
                        h.Cell().Element(Head).AlignCenter().Text("Travaillés");
                        h.Cell().Element(Head).AlignCenter().Text("Absents");
                        h.Cell().Element(Head).AlignCenter().Text("Congé");
                        h.Cell().Element(Head).AlignCenter().Text("Demi");
                        h.Cell().Element(Head).AlignCenter().Text("Retards");
                        h.Cell().Element(Head).AlignRight().Text("Retenue");
                    });

                    foreach (AttendanceSummaryRow r in _m.Rows) Row(table, r, false);
                    Row(table, _m.Totals, true);
                });

                page.Footer().AlignCenter().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
            });
        }

        private static void Row(TableDescriptor table, AttendanceSummaryRow r, bool total)
        {
            System.Func<IContainer, IContainer> cell = total ? (System.Func<IContainer, IContainer>)Tot : Body;
            table.Cell().Element(cell).Text(total ? "TOTAL" : (r.Name ?? string.Empty));
            table.Cell().Element(cell).AlignCenter().Text(Num(r.DaysDue));
            table.Cell().Element(cell).AlignCenter().Text(Num(r.Worked));
            table.Cell().Element(cell).AlignCenter().Text(Num(r.Absent));
            table.Cell().Element(cell).AlignCenter().Text(Num(r.Leave));
            table.Cell().Element(cell).AlignCenter().Text(Num(r.Half));
            table.Cell().Element(cell).AlignCenter().Text(r.Late.ToString());
            table.Cell().Element(cell).AlignRight().Text(r.Deduction.ToString("N2", Fr));
        }

        private static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static IContainer Head(IContainer c) =>
            c.Background("#E8EDF2").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());
        private static IContainer Body(IContainer c) => c.Border(0.5f).BorderColor("#C9D2DC").Padding(4);
        private static IContainer Tot(IContainer c) =>
            c.Background("#F2F5F8").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());
    }

    // ---- 3 · period SUMMARY for the whole company (totals) ----------------

    /// <summary>Whole-company totals for the period: the same figures, totalled onto one page.</summary>
    public sealed class AttendancePeriodSummaryDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private readonly AttendanceSummaryModel _m;
        public AttendancePeriodSummaryDocument(AttendanceSummaryModel m) { _m = m; }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(t => t.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text(_m.CompanyName ?? string.Empty).FontSize(15).SemiBold();
                    col.Item().Text("Synthèse de présence   ·   " + _m.PeriodLabel).FontSize(11);
                });

                page.Content().PaddingTop(18).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text("Effectif : " + _m.Rows.Count + " employé(s)").SemiBold();
                    Line(col, "Jours dus (total)", Num(_m.Totals.DaysDue));
                    Line(col, "Jours travaillés", Num(_m.Totals.Worked));
                    Line(col, "Absences injustifiées", Num(_m.Totals.Absent));
                    Line(col, "Congé / absence justifiée", Num(_m.Totals.Leave));
                    Line(col, "Demi-journées", Num(_m.Totals.Half));
                    Line(col, "Retards", _m.Totals.Late.ToString());
                    col.Item().PaddingTop(6).BorderTop(1).BorderColor("#8A94A2").PaddingTop(8).Row(r =>
                    {
                        r.RelativeItem().Text("Retenue totale (DA)").FontSize(13).SemiBold();
                        r.ConstantItem(160).AlignRight().Text(_m.Totals.Deduction.ToString("N2", Fr)).FontSize(13).SemiBold().FontColor("#B23B3B");
                    });
                });

                page.Footer().AlignCenter().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
            });
        }

        private static void Line(ColumnDescriptor col, string label, string value) =>
            col.Item().Row(r =>
            {
                r.RelativeItem().Text(label);
                r.ConstantItem(160).AlignRight().Text(value).SemiBold();
            });

        private static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
