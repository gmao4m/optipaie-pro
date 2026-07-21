using System;
using System.Globalization;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// A work certificate / attestation (A4), built with the same QuestPDF engine as the
    /// payslip. The body varies by <see cref="CertificateType"/>; every value comes from
    /// the live render model (shared employee + company).
    /// </summary>
    public sealed class CertificateDocument
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly CertificateRenderModel _model;

        public CertificateDocument(CertificateRenderModel model)
        {
            _model = model;
        }

        public void Compose(IDocumentContainer container)
        {
            Company company = _model.Company;
            Employee employee = _model.Employee;
            WorkCertificate certificate = _model.Certificate;
            string fullName = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(11.5f).LineHeight(1.4f));

                page.Header().Column(col =>
                {
                    col.Item().Text(company?.NameFr ?? string.Empty).FontSize(15).SemiBold();
                    if (!string.IsNullOrWhiteSpace(company?.AddressFr))
                        col.Item().Text(company.AddressFr).FontSize(9).FontColor("#555");
                    if (!string.IsNullOrWhiteSpace(company?.Nif))
                        col.Item().Text("NIF : " + company.Nif).FontSize(9).FontColor("#555");
                });

                page.Content().PaddingVertical(24).Column(col =>
                {
                    col.Item().AlignCenter().Text(Heading(certificate.Type)).FontSize(16).SemiBold();
                    if (!string.IsNullOrWhiteSpace(certificate.Reference))
                        col.Item().AlignCenter().PaddingTop(2).Text("Réf. : " + certificate.Reference).FontSize(9).FontColor("#555");

                    col.Item().PaddingTop(28).Text(Body(certificate.Type, fullName, employee));

                    if (!string.IsNullOrWhiteSpace(certificate.Purpose))
                    {
                        col.Item().PaddingTop(14).Text("La présente est délivrée à l'intéressé(e) " + certificate.Purpose + ".");
                    }

                    col.Item().PaddingTop(40).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("Fait à " + City(company) + ", le " +
                            certificate.IssueDate.ToString("dd/MM/yyyy", Fr));
                        c.Item().PaddingTop(46).AlignRight().Text("La Direction").SemiBold();
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("OptiPaie PRO — ").FontSize(8).FontColor("#999");
                    t.Span(company?.NameFr ?? string.Empty).FontSize(8).FontColor("#999");
                });
            });
        }

        private string Body(CertificateType type, string fullName, Employee employee)
        {
            string poste = string.IsNullOrWhiteSpace(employee.Poste) ? "—" : employee.Poste;
            string hire = employee.HireDate.ToString("dd/MM/yyyy", Fr);
            string nss = string.IsNullOrWhiteSpace(employee.Nss) ? string.Empty : " (NSS : " + employee.Nss + ")";
            string seniority = Seniority();

            switch (type)
            {
                case CertificateType.WorkCertificate:
                    return "Nous soussignés, " + (_model.Company?.NameFr ?? "l'employeur") +
                           ", attestons que M./Mme " + fullName + nss +
                           " est employé(e) au sein de notre société en qualité de " + poste +
                           " depuis le " + hire + (seniority.Length > 0 ? ", soit une ancienneté de " + seniority : "") + ".";

                case CertificateType.WorkExperience:
                    string exit = employee.ExitDate.HasValue ? employee.ExitDate.Value.ToString("dd/MM/yyyy", Fr) : "ce jour";
                    return "Nous soussignés, " + (_model.Company?.NameFr ?? "l'employeur") +
                           ", certifions que M./Mme " + fullName + nss +
                           " a été employé(e) au sein de notre société en qualité de " + poste +
                           " du " + hire + " au " + exit +
                           ". Nous lui délivrons le présent certificat de travail pour servir et valoir ce que de droit.";

                case CertificateType.SalaryCertificate:
                    return "Nous soussignés, " + (_model.Company?.NameFr ?? "l'employeur") +
                           ", attestons que M./Mme " + fullName + nss +
                           ", employé(e) en qualité de " + poste + " depuis le " + hire +
                           ", perçoit un salaire de base mensuel de " +
                           _model.MonthlySalary.ToString("N2", Fr) + " DA.";

                default:
                    return _model.Certificate.Body ?? string.Empty;
            }
        }

        private string Seniority()
        {
            if (_model.SeniorityYears <= 0 && _model.SeniorityMonths <= 0) return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            if (_model.SeniorityYears > 0) parts.Add(_model.SeniorityYears + " an" + (_model.SeniorityYears > 1 ? "s" : ""));
            if (_model.SeniorityMonths > 0) parts.Add(_model.SeniorityMonths + " mois");
            return string.Join(" et ", parts);
        }

        private static string Heading(CertificateType type)
        {
            switch (type)
            {
                case CertificateType.WorkCertificate: return "ATTESTATION DE TRAVAIL";
                case CertificateType.WorkExperience: return "CERTIFICAT DE TRAVAIL";
                case CertificateType.SalaryCertificate: return "ATTESTATION DE SALAIRE";
                default: return "ATTESTATION";
            }
        }

        private static string City(Company company)
        {
            return string.IsNullOrWhiteSpace(company?.AddressFr) ? "Alger" : company.AddressFr.Split(',')[0].Trim();
        }
    }
}
