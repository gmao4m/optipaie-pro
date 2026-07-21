using System.Collections.Generic;
using System.Globalization;
using OptiPaie.Core.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Data for the annual-leave balance report.</summary>
    public sealed class LeaveBalanceReportModel
    {
        public string CompanyName { get; set; }
        public int Year { get; set; }
        public List<LeaveBalance> Rows { get; set; } = new List<LeaveBalance>();
    }

    /// <summary>Annual-leave balances (A4), built with the same engine as the payslip.</summary>
    public sealed class LeaveBalanceReportDocument
    {
        private readonly LeaveBalanceReportModel _model;

        public LeaveBalanceReportDocument(LeaveBalanceReportModel model)
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
                    col.Item().Text("Soldes de congés — " + _model.Year.ToString(CultureInfo.InvariantCulture)).FontSize(10);
                });

                page.Content().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(4);
                        c.RelativeColumn(1.6f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.6f);
                        c.RelativeColumn(1.6f);
                        c.RelativeColumn(1.7f);
                        c.RelativeColumn(1.7f);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Element(Head).Text("Employé");
                        h.Cell().Element(Head).AlignRight().Text("Droit");
                        h.Cell().Element(Head).AlignRight().Text("Pris");
                        h.Cell().Element(Head).AlignRight().Text("En attente");
                        h.Cell().Element(Head).AlignRight().Text("Restant");
                        h.Cell().Element(Head).AlignRight().Text("Autres");
                        h.Cell().Element(Head).AlignRight().Text("Sans solde");
                    });

                    foreach (LeaveBalance r in _model.Rows)
                    {
                        table.Cell().Element(Body).Text(r.EmployeeName ?? string.Empty);
                        table.Cell().Element(Body).AlignRight().Text(Num(r.Entitlement));
                        table.Cell().Element(Body).AlignRight().Text(Num(r.Taken));
                        table.Cell().Element(Body).AlignRight().Text(Num(r.Pending));
                        table.Cell().Element(Body).AlignRight().Text(Num(r.Remaining));
                        table.Cell().Element(Body).AlignRight().Text(Num(r.OtherLeaveDays));
                        table.Cell().Element(Body).AlignRight().Text(Num(r.UnpaidDays));
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
