using System.Globalization;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// Loan ledger (A4, print-identical) built with QuestPDF — what was lent, what has been
    /// recovered in the window and all-time, what remains, and which loans are late. Scoped to one
    /// employee or the whole company. Every figure comes from <see cref="LoanLedgerReport"/>, which
    /// derives it from the recorded repayments, so the print can never disagree with the screen.
    /// </summary>
    public sealed class LoanLedgerDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly LoanLedgerReport _report;

        public LoanLedgerDocument(LoanLedgerReport report)
        {
            _report = report;
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
                    col.Item().Text(_report.CompanyName ?? string.Empty).FontSize(14).SemiBold();
                    col.Item().Text("État des prêts et avances — كشف القروض والتسبيقات").FontSize(10);
                    string scope = _report.EmployeeName ?? "Toute l'entreprise · كامل المؤسسة";
                    col.Item().Text(scope + "   ·   " + PeriodLabel()).FontSize(9).FontColor("#555");
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3.2f);   // employee
                            c.RelativeColumn(1.4f);   // type
                            c.RelativeColumn(1.6f);   // lent
                            c.RelativeColumn(1.8f);   // recovered in period
                            c.RelativeColumn(1.8f);   // recovered total
                            c.RelativeColumn(1.6f);   // remaining
                            c.RelativeColumn(1.8f);   // state
                        });

                        table.Header(h =>
                        {
                            h.Cell().Element(Head).Text("Employé");
                            h.Cell().Element(Head).Text("Type");
                            h.Cell().Element(Head).AlignRight().Text("Prêté");
                            h.Cell().Element(Head).AlignRight().Text("Recouvré (pér.)");
                            h.Cell().Element(Head).AlignRight().Text("Recouvré (total)");
                            h.Cell().Element(Head).AlignRight().Text("Restant");
                            h.Cell().Element(Head).Text("État");
                        });

                        foreach (LoanLedgerLine line in _report.Lines)
                        {
                            table.Cell().Element(Body).Text(line.EmployeeName ?? string.Empty);
                            table.Cell().Element(Body).Text(TypeLabel(line.Type));
                            table.Cell().Element(Body).AlignRight().Text(Money(line.Lent));
                            table.Cell().Element(Body).AlignRight().Text(Money(line.RecoveredInPeriod));
                            table.Cell().Element(Body).AlignRight().Text(Money(line.RecoveredTotal));
                            table.Cell().Element(Body).AlignRight().Text(Money(line.Remaining));
                            table.Cell().Element(Body).Text(StateLabel(line))
                                .FontColor(line.IsLate ? "#B23B3B" : "#333");
                        }

                        // Totals row.
                        table.Cell().Element(Total).Text("TOTAL");
                        table.Cell().Element(Total).Text(string.Empty);
                        table.Cell().Element(Total).AlignRight().Text(Money(_report.TotalLent));
                        table.Cell().Element(Total).AlignRight().Text(Money(_report.TotalRecoveredInPeriod));
                        table.Cell().Element(Total).AlignRight().Text(Money(_report.TotalRecovered));
                        table.Cell().Element(Total).AlignRight().Text(Money(_report.TotalRemaining));
                        table.Cell().Element(Total).Text(string.Empty);
                    });

                    if (_report.LateCount > 0)
                    {
                        col.Item().PaddingTop(10).Text(
                            "⚠ " + _report.LateCount + " prêt(s) en retard — une échéance était due sur un mois déjà payé " +
                            "et n'a pas été prélevée. · قروض متأخرة: قسط مستحق في شهر مدفوع ولم يُقتطع.")
                            .FontSize(8.5f).FontColor("#B23B3B");
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

        private string PeriodLabel()
        {
            string from = Fr.DateTimeFormat.GetMonthName(_report.FromMonth) + " " + _report.FromYear;
            string to = Fr.DateTimeFormat.GetMonthName(_report.ToMonth) + " " + _report.ToYear;
            return from == to ? from : from + " → " + to;
        }

        private static string TypeLabel(LoanType type) => type == LoanType.Advance ? "Avance" : "Prêt";

        private static string StateLabel(LoanLedgerLine line)
        {
            if (line.IsLate) return "En retard (" + line.MissedInstallments + ")";
            switch (line.Status)
            {
                case LoanStatus.Active: return "En cours";
                case LoanStatus.Settled: return "Soldé";
                case LoanStatus.Suspended: return "Suspendu";
                case LoanStatus.Cancelled: return "Annulé";
                default: return string.Empty;
            }
        }

        private static string Money(decimal value) => value.ToString("N2", Fr);

        private static IContainer Head(IContainer c) =>
            c.Background("#E8EDF2").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());

        private static IContainer Body(IContainer c) =>
            c.Border(0.5f).BorderColor("#C9D2DC").Padding(4);

        private static IContainer Total(IContainer c) =>
            c.Background("#F2F5F8").Border(0.5f).BorderColor("#8A94A2").Padding(4).DefaultTextStyle(t => t.SemiBold());
    }
}
