using System.Globalization;
using System.Linq;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// The Algerian Fiche de Paie (A4) — a modern bulletin matching the product's
    /// navy/teal identity: a coloured header band, light field-style employee boxes, a
    /// rule-only earnings table (no cell grid, no Code column) with a TOTAL row, a compact
    /// right-aligned summary, and a dominant navy "NET À PAYER" bar.
    ///
    /// PRESENTATION ONLY — it renders the values it is given and computes nothing except
    /// the display TOTAL of the already-computed line gains/retenues. The payroll engine,
    /// its formulas, and every number are unchanged. Fixed layout → prints identically.
    /// </summary>
    public sealed class FichePaieDocument
    {
        // Brand identity (same tokens as the rest of the app).
        private const string Navy = "#1B2A4A";
        private const string Teal = "#0F9B8E";
        private const string Ink = "#1B2430";
        private const string Muted = "#6B7280";
        private const string Divider = "#E4E1DA";
        private const string SoftFill = "#F5F4F0";
        private const string HeadFill = "#F2F1EC";
        private const string White = "#FFFFFF";
        private const string Mono = "Consolas"; // tabular figures (IBM Plex Mono when its font ships)

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly FichePaieModel _m;

        public FichePaieDocument(FichePaieModel model)
        {
            _m = model;
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(26);
                page.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(9).FontColor(Ink));

                page.Content().Column(col =>
                {
                    col.Item().Element(Header);
                    col.Item().PaddingTop(12).Element(EmployeeInfo);
                    col.Item().PaddingTop(12).Element(MainTable);
                    col.Item().PaddingTop(10).Element(Summary);
                    col.Item().PaddingTop(10).Element(NetBar);
                    col.Item().PaddingTop(12).Element(Stamp);
                });
            });
        }

        // -- header ----------------------------------------------------------------

        private void Header(IContainer c)
        {
            c.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.ConstantItem(56).Height(56).AlignMiddle().Element(e =>
                    {
                        if (_m.Company?.Logo != null && _m.Company.Logo.Length > 0)
                        {
                            e.MaxHeight(56).Image(_m.Company.Logo);
                        }
                    });

                    row.RelativeItem().PaddingLeft(16).AlignMiddle().Column(cc =>
                    {
                        cc.Item().Text(CompanyName()).FontSize(17).Bold().FontColor(Navy);
                        cc.Item().PaddingTop(2).Text(CompanyAddress()).FontSize(9).FontColor(Muted);
                        if (!string.IsNullOrWhiteSpace(_m.Company?.CnasEmployerNumber))
                            cc.Item().Text("N° Adhérent CNAS : " + _m.Company.CnasEmployerNumber).FontSize(8.5f).FontColor(Muted);
                    });
                });

                // Full-width brand band: title left, period right.
                col.Item().PaddingTop(10).Background(Navy).PaddingVertical(8).PaddingHorizontal(14).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text("BULLETIN DE PAIE").FontSize(13).Bold().FontColor(White);
                    row.AutoItem().AlignMiddle().Text(t =>
                    {
                        t.Span("Période  ").FontSize(9).FontColor("#B9C2D6");
                        t.Span(PeriodLabel()).FontSize(11).Bold().FontColor(Teal);
                    });
                });
            });
        }

        // -- employee info (light field boxes, no grid) ----------------------------

        private void EmployeeInfo(IContainer c)
        {
            c.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().PaddingRight(9).Column(left =>
                    {
                        left.Item().Element(e => Field(e, "Matricule", Matricule()));
                        left.Item().Element(e => Field(e, "Nom et prénom", EmployeeName()));
                        left.Item().Element(e => Field(e, "Situation familiale", SituationFamiliale()));
                    });
                    row.RelativeItem().PaddingLeft(9).Column(rightc =>
                    {
                        rightc.Item().Element(e => Field(e, "Fonction", Value(_m.Employee?.Poste)));
                        rightc.Item().Element(e => Field(e, "Date d'entrée", HireDate()));
                        rightc.Item().Element(e => Field(e, "N° Sécurité Sociale", Value(_m.Employee?.Nss)));
                    });
                });

                col.Item().Row(row =>
                {
                    row.RelativeItem().PaddingRight(9).Element(e => Field(e, "Affectation", Value(_m.Employee?.Category)));
                    row.RelativeItem().PaddingLeft(9).Element(e => Field(e, "N° Compte", Value(_m.Employee?.Rib)));
                });
            });
        }

        private static void Field(IContainer c, string label, string value)
        {
            c.PaddingBottom(5).Column(col =>
            {
                col.Item().Text(label).FontSize(8).SemiBold().FontColor(Muted);
                col.Item().PaddingTop(2).Background(SoftFill).BorderBottom(1.4f).BorderColor(Divider)
                    .PaddingVertical(4).PaddingHorizontal(9)
                    .Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(10).FontColor(Ink);
            });
        }

        // -- earnings / deductions (rule-only, TOTAL row) --------------------------

        private void MainTable(IContainer c)
        {
            decimal totalGain = _m.Lines.Sum(l => l.Gain ?? 0m);
            decimal totalRetenue = _m.Lines.Sum(l => l.Retenue ?? 0m);

            c.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.6f);
                    cols.RelativeColumn(1.3f);
                    cols.RelativeColumn(1.1f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f);
                });

                // Header — light fill, strong navy underline, no vertical borders.
                Th(table.Cell(), "Libellé", false);
                Th(table.Cell(), "N/Base", true);
                Th(table.Cell(), "Taux", true);
                Th(table.Cell(), "Gain", true);
                Th(table.Cell(), "Retenue", true);

                foreach (FicheLineModel line in _m.Lines)
                {
                    Td(table.Cell(), line.Label, false, false);
                    Td(table.Cell(), line.BaseText, true, true);
                    Td(table.Cell(), line.TauxText, true, true);
                    Td(table.Cell(), MoneyOrEmpty(line.Gain), true, true);
                    Td(table.Cell(), MoneyOrEmpty(line.Retenue), true, true);
                }

                // TOTAL row — bold, navy top rule, sums of the lines above (display only).
                Tt(table.Cell(), "TOTAL", false, false);
                Tt(table.Cell(), string.Empty, true, false);
                Tt(table.Cell(), string.Empty, true, false);
                Tt(table.Cell(), Money(totalGain), true, true);
                Tt(table.Cell(), Money(totalRetenue), true, true);
            });
        }

        private static void Th(IContainer c, string text, bool right)
        {
            IContainer cell = c.Background(HeadFill).BorderBottom(1.2f).BorderColor(Navy).PaddingVertical(5).PaddingHorizontal(8);
            if (right) cell = cell.AlignRight();
            cell.Text(text).FontSize(8.5f).Bold().FontColor(Navy);
        }

        private static void Td(IContainer c, string text, bool right, bool mono)
        {
            IContainer cell = c.BorderBottom(0.6f).BorderColor(Divider).PaddingVertical(4).PaddingHorizontal(8);
            if (right) cell = cell.AlignRight();
            var span = cell.Text(text ?? string.Empty).FontSize(9).FontColor(Ink);
            if (mono) span.FontFamily(Mono);
        }

        private static void Tt(IContainer c, string text, bool right, bool mono)
        {
            IContainer cell = c.BorderTop(1.2f).BorderColor(Navy).PaddingVertical(5).PaddingHorizontal(8);
            if (right) cell = cell.AlignRight();
            var span = cell.Text(text ?? string.Empty).FontSize(9.5f).Bold().FontColor(Ink);
            if (mono) span.FontFamily(Mono);
        }

        // -- summary (compact, right-aligned, no grid) -----------------------------

        private void Summary(IContainer c)
        {
            c.Row(row =>
            {
                row.RelativeItem(1.15f); // whitespace on the left
                row.RelativeItem(1f).Column(s =>
                {
                    SummaryLine(s, "Salaire brut", _m.SalaireBrut);
                    SummaryLine(s, "Salaire cotisable", _m.BaseCotisable);
                    SummaryLine(s, "Salaire imposable", _m.BaseImposable);
                    SummaryLine(s, "CNAS", _m.CnasEmployee);
                    SummaryLine(s, "Abattement", _m.Abattement);
                    SummaryLine(s, "IRG", _m.Irg);
                });
            });
        }

        private static void SummaryLine(ColumnDescriptor col, string label, decimal value)
        {
            col.Item().BorderBottom(0.6f).BorderColor(Divider).PaddingVertical(3).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(label).FontSize(9.5f).FontColor(Muted);
                row.ConstantItem(120).AlignRight().AlignMiddle()
                    .Text(Money(value) + " DA").FontFamily(Mono).FontSize(9.5f).FontColor(Ink);
            });
        }

        // -- net à payer (dominant navy bar) ---------------------------------------

        private void NetBar(IContainer c)
        {
            c.Background(Navy).PaddingVertical(11).PaddingHorizontal(16).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text("NET À PAYER").FontSize(14).Bold().FontColor(White);
                row.AutoItem().AlignMiddle()
                    .Text(Money(_m.NetSalaire) + " DA").FontFamily(Mono).FontSize(18).Bold().FontColor(White);
            });
        }

        // -- stamp (small, bottom-right) -------------------------------------------

        private void Stamp(IContainer c)
        {
            c.Row(row =>
            {
                row.RelativeItem(2.4f);
                row.RelativeItem(1f).Border(0.75f).BorderColor(Divider).MinHeight(48).Padding(8)
                    .Text("Cachet de l'entreprise").FontSize(8).FontColor(Muted);
            });
        }

        // -- text helpers (unchanged formatting) -----------------------------------

        private static string Money(decimal v) => v.ToString("N2", Fr);
        private static string MoneyOrEmpty(decimal? v) => v.HasValue ? v.Value.ToString("N2", Fr) : string.Empty;
        private static string Value(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

        private string CompanyName()
        {
            Company c = _m.Company;
            if (c == null) return string.Empty;
            return _m.IsArabic && !string.IsNullOrWhiteSpace(c.NameAr) ? c.NameAr : c.NameFr;
        }

        private string CompanyAddress()
        {
            Company c = _m.Company;
            if (c == null) return string.Empty;
            return _m.IsArabic && !string.IsNullOrWhiteSpace(c.AddressAr) ? c.AddressAr : (c.AddressFr ?? string.Empty);
        }

        private string EmployeeName()
        {
            Employee e = _m.Employee;
            if (e == null) return string.Empty;
            return _m.IsArabic && !string.IsNullOrWhiteSpace(e.LastNameAr)
                ? (e.LastNameAr + " " + e.FirstNameAr).Trim()
                : (e.LastNameFr + " " + e.FirstNameFr).Trim();
        }

        private string Matricule()
        {
            Employee e = _m.Employee;
            return (e == null || e.Id <= 0) ? "—" : e.Id.ToString("0000", Fr);
        }

        private string HireDate()
        {
            return _m.Employee == null ? "—" : _m.Employee.HireDate.ToString("dd/MM/yyyy", Fr);
        }

        private string SituationFamiliale()
        {
            if (_m.Employee == null) return "—";
            switch (_m.Employee.MaritalStatus)
            {
                case MaritalStatus.Single: return "Célibataire";
                case MaritalStatus.Married: return "Marié(e)";
                case MaritalStatus.Divorced: return "Divorcé(e)";
                case MaritalStatus.Widowed: return "Veuf(ve)";
                default: return "—";
            }
        }

        private string PeriodLabel()
        {
            if (_m.Month < 1 || _m.Month > 12)
            {
                return _m.Month.ToString("00", CultureInfo.InvariantCulture) + "/" + _m.Year;
            }

            string month = Fr.DateTimeFormat.GetMonthName(_m.Month);
            if (month.Length > 0)
            {
                month = char.ToUpper(month[0], Fr) + month.Substring(1);
            }

            return month + " " + _m.Year;
        }
    }
}
