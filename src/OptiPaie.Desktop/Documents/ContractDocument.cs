using System;
using System.Globalization;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Data for the employment-contract document.</summary>
    public sealed class ContractDocumentModel
    {
        public Company Company { get; set; }
        public Employee Employee { get; set; }
        public EmploymentContract Contract { get; set; }
    }

    /// <summary>
    /// An employment contract (A4), built with the same QuestPDF engine as the payslip.
    /// A straightforward French "contrat de travail" carrying the agreed terms.
    /// </summary>
    public sealed class ContractDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly ContractDocumentModel _model;

        public ContractDocument(ContractDocumentModel model)
        {
            _model = model;
        }

        public void Compose(IDocumentContainer container)
        {
            Company company = _model.Company;
            Employee employee = _model.Employee;
            EmploymentContract contract = _model.Contract;

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Sans).FontSize(10.5f));

                page.Header().Column(col =>
                {
                    col.Item().Text(company?.NameFr ?? string.Empty).FontSize(15).SemiBold();
                    if (!string.IsNullOrWhiteSpace(company?.AddressFr))
                        col.Item().Text(company.AddressFr).FontSize(9).FontColor("#555");
                    col.Item().PaddingTop(10).AlignCenter()
                        .Text(Title(contract)).FontSize(16).SemiBold();
                });

                page.Content().PaddingVertical(14).Column(col =>
                {
                    col.Spacing(9);

                    col.Item().Text(t =>
                    {
                        t.Span("Entre les soussignés : ").SemiBold();
                        t.Span(company?.NameFr ?? "l'employeur");
                        t.Span(", ci-après « l'employeur »,");
                    });
                    col.Item().Text("et");
                    col.Item().Text(t =>
                    {
                        t.Span("M./Mme ").SemiBold();
                        t.Span((employee?.LastNameFr + " " + employee?.FirstNameFr).Trim());
                        if (!string.IsNullOrWhiteSpace(employee?.Nss))
                            t.Span(" (NSS : " + employee.Nss + ")");
                        t.Span(", ci-après « le salarié ».");
                    });

                    col.Item().PaddingTop(6).Text("Il a été convenu ce qui suit :").SemiBold();

                    Article(col, "Article 1 — Nature du contrat",
                        "Le présent contrat est conclu à titre de " + EnumLabels.ContractLabel(contract.Type) +
                        (contract.EndDate.HasValue
                            ? ", pour une durée déterminée."
                            : ", pour une durée indéterminée."));

                    Article(col, "Article 2 — Fonctions",
                        "Le salarié est engagé en qualité de " +
                        (string.IsNullOrWhiteSpace(contract.Position) ? "—" : contract.Position) + ".");

                    Article(col, "Article 3 — Prise d'effet" + (contract.EndDate.HasValue ? " et terme" : ""),
                        "Le contrat prend effet le " + Date(contract.StartDate) +
                        (contract.EndDate.HasValue ? " et prend fin le " + Date(contract.EndDate.Value) + "." : "."));

                    if (contract.TrialPeriodDays > 0)
                    {
                        Article(col, "Article 4 — Période d'essai",
                            "Le contrat est assorti d'une période d'essai de " + contract.TrialPeriodDays + " jours.");
                    }

                    Article(col, "Article 5 — Rémunération",
                        "Le salarié percevra un salaire de base mensuel de " +
                        contract.BaseSalary.ToString("N2", Fr) + " DA, payable selon la réglementation en vigueur.");

                    if (!string.IsNullOrWhiteSpace(contract.Reference))
                    {
                        col.Item().PaddingTop(4).Text("Référence : " + contract.Reference).FontSize(9).FontColor("#555");
                    }

                    col.Item().PaddingTop(28).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("L'employeur").SemiBold();
                            c.Item().PaddingTop(40).Text("_______________________");
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("Le salarié").SemiBold();
                            c.Item().PaddingTop(40).AlignRight().Text("_______________________");
                        });
                    });

                    col.Item().PaddingTop(16).AlignRight()
                        .Text("Fait le " + Date(contract.SignedDate ?? DateTime.Today)).FontSize(9).FontColor("#555");
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("OptiPaie PRO — ").FontSize(8).FontColor("#999");
                    t.Span(company?.NameFr ?? string.Empty).FontSize(8).FontColor("#999");
                });
            });
        }

        private static void Article(ColumnDescriptor col, string heading, string body)
        {
            col.Item().PaddingTop(4).Text(heading).SemiBold();
            col.Item().Text(body);
        }

        private static string Title(EmploymentContract contract)
        {
            return contract.EndDate.HasValue
                ? "CONTRAT DE TRAVAIL À DURÉE DÉTERMINÉE"
                : "CONTRAT DE TRAVAIL À DURÉE INDÉTERMINÉE";
        }

        private static string Date(DateTime value) => value.ToString("dd/MM/yyyy", Fr);
    }
}
