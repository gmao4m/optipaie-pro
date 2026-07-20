using System.Globalization;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// The Algerian Fiche de Paie — a sober, professional A4 bulletin with black-bordered
    /// tables, styled like the payroll software Algerian companies use. Fixed layout →
    /// prints identically on every printer. Renders values only; computes nothing.
    /// </summary>
    public sealed class FichePaieDocument
    {
        private const string Ink = "#1A1A1A";
        private const string Muted = "#555555";
        private const string HeaderFill = "#ECECEC";

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
                page.Margin(32);
                page.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(9).FontColor(Ink));

                page.Content().Column(col =>
                {
                    col.Item().Element(Header);
                    col.Item().PaddingTop(12).Element(EmployeeInfo);
                    col.Item().PaddingTop(12).Element(MainTable);
                    col.Item().PaddingTop(12).Element(Summary);
                    col.Item().PaddingTop(22).Element(Stamp);
                });
            });
        }

        private void Header(IContainer c)
        {
            c.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Row(r =>
                    {
                        r.ConstantItem(52).Element(e =>
                        {
                            if (_m.Company?.Logo != null && _m.Company.Logo.Length > 0)
                            {
                                e.Height(52).Image(_m.Company.Logo);
                            }
                        });
                        r.RelativeItem().PaddingLeft(10).Column(cc =>
                        {
                            cc.Item().Text(CompanyName()).FontSize(13).Bold().FontColor(Ink);
                            cc.Item().Text(CompanyAddress()).FontSize(8.5f).FontColor(Muted);
                            cc.Item().PaddingTop(1).Text("N° Adhérent : " + Value(_m.Company?.CnasEmployerNumber)).FontSize(8.5f).FontColor(Muted);
                        });
                    });

                    row.ConstantItem(180).Column(cc =>
                    {
                        cc.Item().AlignRight().Text("FICHE DE PAIE").FontSize(15).Bold().FontColor(Ink);
                        cc.Item().PaddingTop(2).AlignRight().Text("Période : " + PeriodLabel()).FontSize(9).FontColor(Ink);
                    });
                });

                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Ink);
            });
        }

        private void EmployeeInfo(IContainer c)
        {
            c.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1.6f);
                    cols.RelativeColumn(2.3f);
                    cols.RelativeColumn(1.6f);
                    cols.RelativeColumn(2.3f);
                });

                Lab(table.Cell(), "Matricule"); Val(table.Cell(), Matricule());
                Lab(table.Cell(), "Nom et prénom"); Val(table.Cell(), EmployeeName());
                Lab(table.Cell(), "Fonction"); Val(table.Cell(), Value(_m.Employee?.Poste));
                Lab(table.Cell(), "Situation familiale"); Val(table.Cell(), SituationFamiliale());
                Lab(table.Cell(), "Affectation"); Val(table.Cell(), Value(_m.Employee?.Category));
                Lab(table.Cell(), "N° Sécurité Sociale"); Val(table.Cell(), Value(_m.Employee?.Nss));
                Lab(table.Cell(), "N° Compte"); Val(table.Cell(), Value(_m.Employee?.Rib));
                Lab(table.Cell(), "Période"); Val(table.Cell(), PeriodLabel());
            });
        }

        private void MainTable(IContainer c)
        {
            c.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.4f);
                    cols.RelativeColumn(1.3f);
                    cols.RelativeColumn(1.1f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f);
                });

                Th(table.Cell(), "Libellé", false);
                Th(table.Cell(), "N/Base", true);
                Th(table.Cell(), "Taux", true);
                Th(table.Cell(), "Gain", true);
                Th(table.Cell(), "Retenue", true);

                foreach (FicheLineModel line in _m.Lines)
                {
                    Td(table.Cell(), line.Label, false);
                    Td(table.Cell(), line.BaseText, true);
                    Td(table.Cell(), line.TauxText, true);
                    Td(table.Cell(), MoneyOrEmpty(line.Gain), true);
                    Td(table.Cell(), MoneyOrEmpty(line.Retenue), true);
                }
            });
        }

        private void Summary(IContainer c)
        {
            c.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(4f);
                    cols.RelativeColumn(1.4f);
                });

                Rk(table, "Salaire brut", _m.SalaireBrut);
                Rk(table, "Salaire cotisable", _m.BaseCotisable);
                Rk(table, "Salaire imposable", _m.BaseImposable);
                Rk(table, "CNAS", _m.CnasEmployee);
                Rk(table, "Abattement", _m.Abattement);
                Rk(table, "IRG", _m.Irg);

                table.Cell().Background(Ink).PaddingVertical(9).PaddingHorizontal(9)
                    .Text("NET À PAYER").FontSize(13).Bold().FontColor("#FFFFFF");
                table.Cell().Background(Ink).PaddingVertical(9).PaddingHorizontal(9).AlignRight()
                    .Text(Money(_m.NetSalaire) + " DA").FontSize(15).Bold().FontColor("#FFFFFF");
            });
        }

        private void Stamp(IContainer c)
        {
            c.Row(row =>
            {
                row.RelativeItem(2f);
                row.RelativeItem(1f).Border(0.75f).BorderColor(Ink).MinHeight(72).Padding(8)
                    .Text("Cachet de l'entreprise").FontSize(8.5f).FontColor(Muted);
            });
        }

        // -- cell helpers ----------------------------------------------------------

        private static void Lab(IContainer c, string text)
        {
            c.Border(0.75f).BorderColor(Ink).Background(HeaderFill).PaddingVertical(4).PaddingHorizontal(6)
                .Text(text).FontSize(8).Bold().FontColor(Ink);
        }

        private static void Val(IContainer c, string text)
        {
            c.Border(0.75f).BorderColor(Ink).PaddingVertical(4).PaddingHorizontal(6)
                .Text(text ?? string.Empty).FontSize(9).FontColor(Ink);
        }

        private static void Th(IContainer c, string text, bool right)
        {
            IContainer cell = c.Border(0.75f).BorderColor(Ink).Background(HeaderFill).PaddingVertical(5).PaddingHorizontal(6);
            if (right) cell = cell.AlignRight();
            cell.Text(text).FontSize(8.5f).Bold().FontColor(Ink);
        }

        private static void Td(IContainer c, string text, bool right)
        {
            IContainer cell = c.Border(0.75f).BorderColor(Ink).PaddingVertical(4).PaddingHorizontal(6);
            if (right) cell = cell.AlignRight();
            cell.Text(text ?? string.Empty).FontSize(8.5f).FontColor(Ink);
        }

        private static void Rk(TableDescriptor table, string label, decimal value)
        {
            table.Cell().Border(0.75f).BorderColor(Ink).PaddingVertical(4).PaddingHorizontal(6)
                .Text(label).FontSize(8.5f).FontColor(Ink);
            table.Cell().Border(0.75f).BorderColor(Ink).PaddingVertical(4).PaddingHorizontal(6)
                .AlignRight().Text(Money(value)).FontSize(8.5f).FontColor(Ink);
        }

        // -- text helpers ----------------------------------------------------------

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
