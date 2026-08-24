using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Payroll;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// The Algerian Fiche de Paie (A4, one page). EVERYTHING lives in a single table, in the
    /// order a comptable reads it: earnings → SALAIRE BRUT → salaire soumis à cotisation →
    /// CNAS → SALAIRE IMPOSABLE → IRG → other deductions → TOTAL GAINS / TOTAL RETENUES →
    /// NET À PAYER. Base and Taux are shown on every line that has them (CNAS/IRG included).
    ///
    /// The abattement does NOT appear anywhere — it stays inside the engine's IRG computation.
    /// The slip is self-verifying: NET À PAYER = TOTAL GAINS − TOTAL RETENUES, with no hidden term.
    ///
    /// PRESENTATION ONLY — every number comes from the already-computed model; the payroll engine
    /// and its formulas are untouched, so the net is byte-for-byte the same as before.
    /// </summary>
    public sealed class FichePaieDocument
    {
        private const string Navy = "#0E3B2C";   // deep emerald — header/net band
        private const string Teal = "#E3B341";   // warm gold — period highlight
        private const string Ink = "#182B26";
        private const string Muted = "#5B6B66";
        private const string Divider = "#E4E1DA";
        private const string SoftFill = "#F5F4F0";
        private const string BandFill = "#D5E4DA";   // intermediate-total bands (distinct from normal rows)
        private const string HeadFill = "#F2F1EC";
        private const string White = "#FFFFFF";
        private const string Mono = PdfFonts.Mono;

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly FichePaieModel _m;

        // CACOBATPH overlay (BTPH sector, opt-in): the employee share is a real deduction, so it
        // appears as its own retenue line ONLY when enabled — keeping the net verifiable.
        private readonly bool _cacobatphOn;
        private readonly CacobatphResult _cacobatph;

        public FichePaieDocument(FichePaieModel model)
        {
            _m = model;
            _cacobatphOn = model.Company != null && model.Company.BtphSector && model.Company.CacobatphEnabled;
            _cacobatph = _cacobatphOn ? CacobatphCalculator.Compute(model.BaseCotisable) : null;
        }

        private decimal CacobatphEmployee => _cacobatphOn ? _cacobatph.EmployeeTotal : 0m;

        private List<FicheLineModel> GainLines => _m.Lines.Where(l => l.Gain.HasValue).ToList();
        private List<FicheLineModel> RetenueLines => _m.Lines.Where(l => l.Retenue.HasValue).ToList();

        private decimal TotalGains => GainLines.Sum(l => l.Gain ?? 0m);
        private decimal TotalRetenues =>
            _m.CnasEmployee + _m.Irg + CacobatphEmployee + RetenueLines.Sum(l => l.Retenue ?? 0m);
        private decimal NetToPay => TotalGains - TotalRetenues;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontFamily(_m.IsArabic ? PdfFonts.SansArabic : PdfFonts.Sans).FontSize(9).FontColor(Ink));

                page.Content().Column(col =>
                {
                    col.Item().Element(Header);
                    col.Item().PaddingTop(10).Element(EmployeeInfo);
                    col.Item().PaddingTop(10).Element(MainTable);
                    col.Item().PaddingTop(8).Element(NetBar);
                    col.Item().PaddingTop(16).Element(Stamp);
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
                    if (_m.Company?.Logo != null && _m.Company.Logo.Length > 0)
                        row.ConstantItem(50).Height(50).AlignMiddle().MaxHeight(50).Image(_m.Company.Logo);

                    row.RelativeItem().PaddingLeft(_m.Company?.Logo != null ? 14 : 0).AlignMiddle().Column(cc =>
                    {
                        cc.Item().Text(CompanyName()).FontSize(16).Bold().FontColor(Navy);
                        cc.Item().PaddingTop(1).Text(CompanyAddress()).FontSize(9).FontColor(Muted);
                        if (!string.IsNullOrWhiteSpace(_m.Company?.CnasEmployerNumber))
                            cc.Item().Text("N° Adhérent CNAS : " + _m.Company.CnasEmployerNumber).FontSize(8.5f).FontColor(Muted);
                    });
                });

                col.Item().PaddingTop(9).Background(Navy).PaddingVertical(7).PaddingHorizontal(14).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text("BULLETIN DE PAIE").FontSize(13).Bold().FontColor(White);
                    row.AutoItem().AlignMiddle().Text(t =>
                    {
                        t.Span("Période  ").FontSize(9).FontColor("#CDEADF");
                        t.Span(PeriodLabel()).FontSize(11).Bold().FontColor(Teal);
                    });
                });
            });
        }

        // -- employee identity (two columns, label : value) ------------------------

        private void EmployeeInfo(IContainer c)
        {
            c.Border(0.75f).BorderColor(Divider).Background(SoftFill).Padding(2).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    Id(left, "Matricule", Matricule());
                    Id(left, "Nom et prénom", EmployeeName());
                    Id(left, "N° Sécurité Soc.", Value(_m.Employee?.Nss));
                    Id(left, "Situation fam.", SituationFamiliale());
                });
                row.RelativeItem().Column(rightc =>
                {
                    Id(rightc, "Fonction", Value(_m.Employee?.Poste));
                    Id(rightc, "Catégorie", Value(_m.Employee?.Category));
                    Id(rightc, "Date d'entrée", HireDate());
                    Id(rightc, "N° Compte", Value(_m.Employee?.Rib));
                });
            });
        }

        private static void Id(ColumnDescriptor col, string label, string value)
        {
            col.Item().PaddingVertical(2).PaddingHorizontal(8).Row(row =>
            {
                row.ConstantItem(96).Text(label).FontSize(8.5f).SemiBold().FontColor(Muted);
                row.AutoItem().Text(":").FontSize(8.5f).FontColor(Muted);
                row.RelativeItem().PaddingLeft(6).Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(9).FontColor(Ink);
            });
        }

        // -- the single table ------------------------------------------------------

        private void MainTable(IContainer c)
        {
            c.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.5f);  // Libellé
                    cols.RelativeColumn(1.3f);  // Base
                    cols.RelativeColumn(1.1f);  // Taux
                    cols.RelativeColumn(1.5f);  // Gain
                    cols.RelativeColumn(1.5f);  // Retenue
                });

                // EVERY cell is positioned explicitly (Row + Column). The auto-placement cursor,
                // combined with the full-width ColumnSpan bands, was fragile and could put a
                // value on the wrong row (label/amount mismatch). Explicit coordinates make the
                // layout deterministic and identical in the PDF and image backends.
                uint r = 1;

                Th(table.Cell().Row(r).Column(1), "Libellé", false);
                Th(table.Cell().Row(r).Column(2), "Base", true);
                Th(table.Cell().Row(r).Column(3), "Taux", true);
                Th(table.Cell().Row(r).Column(4), "Gain", true);
                Th(table.Cell().Row(r).Column(5), "Retenue", true);
                r++;

                // 1) Earnings
                foreach (FicheLineModel l in GainLines)
                    RowAt(table, r++, l.Label, l.BaseText, l.TauxText, l.Gain, null);

                // 2) SALAIRE BRUT + salaire soumis à cotisation
                BandAt(table, r++, "SALAIRE BRUT", TotalGains, true);
                BandAt(table, r++, "Salaire soumis à cotisation", _m.BaseCotisable, false);

                // 3) CNAS (base + taux shown) [+ CACOBATPH employee share when enabled]
                RowAt(table, r++, "Retenue CNAS", Money(_m.BaseCotisable), "9 %", null, _m.CnasEmployee);
                if (_cacobatphOn)
                    RowAt(table, r++, "CACOBATPH (part salarié)", Money(_m.BaseCotisable), null, null, _cacobatph.EmployeeTotal);

                // 4) SALAIRE IMPOSABLE
                BandAt(table, r++, "SALAIRE IMPOSABLE", _m.BaseImposable, false);

                // 5) IRG (alone on its row) + other deductions
                RowAt(table, r++, "IRG", Money(_m.BaseImposable), "barème", null, _m.Irg);
                foreach (FicheLineModel l in RetenueLines)
                    RowAt(table, r++, l.Label, l.BaseText, l.TauxText, null, l.Retenue);

                // 6) TOTAL GAINS | TOTAL RETENUES (two halves, full width)
                table.Cell().Row(r).Column(1).ColumnSpan(5).BorderTop(1.3f).BorderColor(Navy).PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Background(HeadFill).PaddingVertical(6).PaddingHorizontal(10).Row(rr =>
                    {
                        rr.RelativeItem().Text("TOTAL GAINS").FontSize(10).Bold().FontColor(Navy);
                        rr.AutoItem().Text(Money(TotalGains)).FontFamily(Mono).FontSize(10.5f).Bold().FontColor(Ink);
                    });
                    row.ConstantItem(10);
                    row.RelativeItem().Background(HeadFill).PaddingVertical(6).PaddingHorizontal(10).Row(rr =>
                    {
                        rr.RelativeItem().Text("TOTAL RETENUES").FontSize(10).Bold().FontColor(Navy);
                        rr.AutoItem().Text(Money(TotalRetenues)).FontFamily(Mono).FontSize(10.5f).Bold().FontColor(Ink);
                    });
                });
            });
        }

        private static void Th(IContainer c, string text, bool right)
        {
            IContainer cell = c.Background(HeadFill).BorderBottom(1.2f).BorderColor(Navy).PaddingVertical(5).PaddingHorizontal(8);
            if (right) cell = cell.AlignRight();
            cell.Text(text).FontSize(8.5f).Bold().FontColor(Navy);
        }

        /// <summary>One data line at row <paramref name="r"/>: Libellé | Base | Taux | Gain | Retenue.
        /// Label and amount share the SAME row by construction (explicit column indices).</summary>
        private void RowAt(TableDescriptor table, uint r, string label, string baseText, string tauxText, decimal? gain, decimal? retenue)
        {
            Cell(table.Cell().Row(r).Column(1), label, false, false);
            Cell(table.Cell().Row(r).Column(2), baseText, true, true);
            Cell(table.Cell().Row(r).Column(3), tauxText, true, false);
            Cell(table.Cell().Row(r).Column(4), gain.HasValue ? Money(gain.Value) : string.Empty, true, true);
            Cell(table.Cell().Row(r).Column(5), retenue.HasValue ? Money(retenue.Value) : string.Empty, true, true);
        }

        private static void Cell(IContainer c, string text, bool right, bool mono)
        {
            IContainer cell = c.BorderBottom(0.6f).BorderColor(Divider).PaddingVertical(3.5f).PaddingHorizontal(8);
            if (right) cell = cell.AlignRight();
            var span = cell.Text(text ?? string.Empty).FontSize(9).FontColor(Ink);
            if (mono) span.FontFamily(Mono);
        }

        /// <summary>A full-width intermediate-total band spanning every column, at row <paramref name="r"/>.
        /// SALAIRE BRUT is a filled navy bar; the other two are clearly banded (medium fill + navy
        /// rules top and bottom) so they never blend with an ordinary line.</summary>
        private void BandAt(TableDescriptor table, uint r, string label, decimal amount, bool strong)
        {
            IContainer cell = table.Cell().Row(r).Column(1).ColumnSpan(5);
            cell = strong
                ? cell.Background(Navy)
                : cell.Background(BandFill).BorderTop(1f).BorderBottom(1f).BorderColor(Navy);
            cell.PaddingVertical(strong ? 5.5f : 5f).PaddingHorizontal(10).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(label).FontSize(strong ? 10.5f : 9.5f).Bold()
                    .FontColor(strong ? White : Navy);
                row.AutoItem().AlignMiddle().Text(Money(amount) + " DA").FontFamily(Mono).FontSize(strong ? 11 : 10).Bold()
                    .FontColor(strong ? White : Navy);
            });
        }

        // -- net à payer -----------------------------------------------------------

        private void NetBar(IContainer c)
        {
            c.Background(Navy).PaddingVertical(11).PaddingHorizontal(16).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text("NET À PAYER").FontSize(14).Bold().FontColor(White);
                row.AutoItem().AlignMiddle().Text(Money(NetToPay) + " DA").FontFamily(Mono).FontSize(18).Bold().FontColor(White);
            });
        }

        private void Stamp(IContainer c)
        {
            c.Row(row =>
            {
                row.RelativeItem(2.2f);
                row.RelativeItem(1f).AlignCenter().Column(col =>
                {
                    col.Item().Height(46);
                    col.Item().BorderTop(0.75f).BorderColor(Divider).PaddingTop(4).AlignCenter()
                        .Text("Cachet et signature\nde l'employeur").FontSize(8.5f).FontColor(Muted);
                });
            });
        }

        // -- helpers ---------------------------------------------------------------

        private static string Money(decimal v) => v.ToString("N2", Fr);
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

        private string HireDate() => _m.Employee == null ? "—" : _m.Employee.HireDate.ToString("dd/MM/yyyy", Fr);

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
                return _m.Month.ToString("00", CultureInfo.InvariantCulture) + "/" + _m.Year;

            string month = Fr.DateTimeFormat.GetMonthName(_m.Month);
            if (month.Length > 0) month = char.ToUpper(month[0], Fr) + month.Substring(1);
            return month + " " + _m.Year;
        }
    }
}
