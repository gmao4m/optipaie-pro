using System.Collections.Generic;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Flat, pre-formatted snapshot of one employee's file, for the dossier PDF.</summary>
    public sealed class EmployeeDossier
    {
        public string CompanyName { get; set; }
        public string FullName { get; set; }
        public string Poste { get; set; }
        public string Department { get; set; }
        public string Matricule { get; set; }
        public string HireDate { get; set; }
        public string ContractType { get; set; }
        public string Status { get; set; }

        public string AttendancePeriod { get; set; }
        public string Present { get; set; }
        public string Absent { get; set; }
        public string Late { get; set; }
        public string OnLeave { get; set; }

        public string LeaveEntitlement { get; set; }
        public string LeaveTaken { get; set; }
        public string LeaveRemaining { get; set; }
        public string LeavePending { get; set; }

        public string LatestScore { get; set; }

        public List<string[]> Contracts { get; set; } = new List<string[]>();
        public List<string[]> Payslips { get; set; } = new List<string[]>();
        public List<string[]> Leave { get; set; } = new List<string[]>();
        public List<string[]> Loans { get; set; } = new List<string[]>();
        public List<string[]> Assets { get; set; } = new List<string[]>();
        public List<string[]> Training { get; set; } = new List<string[]>();
        public List<string[]> Goals { get; set; } = new List<string[]>();
        public List<string[]> Career { get; set; } = new List<string[]>();
    }

    /// <summary>
    /// "Dossier employé" — a single consolidated PDF of one person's file across every
    /// module, for audits and HR records. Built from the already-aggregated profile data;
    /// it computes nothing.
    /// </summary>
    public sealed class EmployeeDossierDocument
    {
        private const string Navy = "#1B2A4A";
        private const string Teal = "#0F9B8E";
        private const string Ink = "#1D2733";
        private const string Muted = "#6B7480";
        private const string HeadFill = "#F2F1EC";
        private const string Line = "#D9DCE1";

        private readonly EmployeeDossier _d;

        public EmployeeDossierDocument(EmployeeDossier dossier) { _d = dossier; }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Sans).FontSize(9).FontColor(Ink));

                page.Header().Element(Header);
                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(12);
                    col.Item().Element(Identity);
                    col.Item().Element(Snapshot);
                    Section(col, "Contrats", new[] { "Type", "Poste", "Salaire", "Période", "Statut" }, _d.Contracts, new float[] { 1, 2, 1.3f, 2, 1.2f });
                    Section(col, "Paie — derniers bulletins", new[] { "Période", "Net à payer" }, _d.Payslips, new float[] { 2, 2 });
                    Section(col, "Congés", new[] { "Type", "Période", "Jours", "Statut" }, _d.Leave, new float[] { 1.5f, 2.4f, 0.8f, 1.2f });
                    Section(col, "Prêts & avances", new[] { "Montant", "Mensualité", "Reste dû", "Statut" }, _d.Loans, new float[] { 1.4f, 1.4f, 1.4f, 1.2f });
                    Section(col, "Matériel détenu / historique", new[] { "Bien", "Attribué", "Rendu", "Statut" }, _d.Assets, new float[] { 2.4f, 1.3f, 1.3f, 1.1f });
                    Section(col, "Formation", new[] { "Formation", "Organisme", "Date", "Résultat", "Attestation" }, _d.Training, new float[] { 2, 1.6f, 1.1f, 1.2f, 1.4f });
                    Section(col, "Objectifs", new[] { "Objectif", "Avancement", "Statut" }, _d.Goals, new float[] { 3, 1.2f, 1.3f });
                    Section(col, "Parcours de carrière", new[] { "Date", "Événement", "Détail" }, _d.Career, new float[] { 1.2f, 2, 3 });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Dossier employé · ").FontSize(7.5f).FontColor(Muted);
                    t.Span(_d.CompanyName ?? "").FontSize(7.5f).FontColor(Muted);
                    t.Span("   ·   page ").FontSize(7.5f).FontColor(Muted);
                    t.CurrentPageNumber().FontSize(7.5f).FontColor(Muted);
                    t.Span(" / ").FontSize(7.5f).FontColor(Muted);
                    t.TotalPages().FontSize(7.5f).FontColor(Muted);
                });
            });
        }

        private void Header(IContainer c)
        {
            c.Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("DOSSIER EMPLOYÉ").FontFamily(PdfFonts.Sans).FontSize(16).Bold().FontColor(Navy);
                    col.Item().Text(_d.CompanyName ?? "").FontSize(9).FontColor(Muted);
                });
                row.ConstantItem(120).AlignRight().Text(_d.Status ?? "").FontSize(9).SemiBold().FontColor(Teal);
            });
        }

        private void Identity(IContainer c)
        {
            c.Border(0.75f).BorderColor(Line).Background(HeadFill).Padding(12).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(_d.FullName ?? "").FontSize(14).Bold().FontColor(Navy);
                    col.Item().PaddingTop(2).Text((_d.Poste ?? "") + "  ·  " + (_d.Department ?? "")).FontSize(9.5f).FontColor(Ink);
                });
                row.ConstantItem(230).Column(col =>
                {
                    Kv(col, "Matricule", _d.Matricule);
                    Kv(col, "Embauche", _d.HireDate);
                    Kv(col, "Contrat", _d.ContractType);
                });
            });
        }

        private void Snapshot(IContainer c)
        {
            c.Row(row =>
            {
                row.RelativeItem().Element(x => Card(x, "Présence — " + (_d.AttendancePeriod ?? ""),
                    "Présents " + _d.Present + " · Absents " + _d.Absent + " · Retards " + _d.Late + " · Congés " + _d.OnLeave));
                row.ConstantItem(8);
                row.RelativeItem().Element(x => Card(x, "Congés (solde)",
                    "Droit " + _d.LeaveEntitlement + " · Pris " + _d.LeaveTaken + " · Restant " + _d.LeaveRemaining + " · En attente " + _d.LeavePending));
                row.ConstantItem(8);
                row.RelativeItem().Element(x => Card(x, "Dernière évaluation", _d.LatestScore ?? "Aucune"));
            });
        }

        private static void Card(IContainer c, string title, string body)
        {
            c.Border(0.75f).BorderColor(Line).Padding(9).Column(col =>
            {
                col.Item().Text(title).FontSize(8).FontColor(Muted);
                col.Item().PaddingTop(3).Text(body).FontSize(9).FontColor(Ink);
            });
        }

        private static void Kv(ColumnDescriptor col, string k, string v)
        {
            col.Item().Row(r =>
            {
                r.ConstantItem(80).Text(k).FontSize(8.5f).FontColor(Muted);
                r.RelativeItem().Text(v ?? "—").FontSize(9).FontColor(Ink);
            });
        }

        private void Section(ColumnDescriptor parent, string title, string[] headers, List<string[]> rows, float[] widths)
        {
            parent.Item().Column(col =>
            {
                col.Item().PaddingBottom(4).Text(title).FontSize(11).SemiBold().FontColor(Navy);

                if (rows == null || rows.Count == 0)
                {
                    col.Item().Border(0.5f).BorderColor(Line).Padding(8).Text("Aucune donnée pour cet employé.").FontSize(8.5f).Italic().FontColor(Muted);
                    return;
                }

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd => { foreach (float w in widths) cd.RelativeColumn(w); });
                    table.Header(h =>
                    {
                        foreach (string head in headers)
                            h.Cell().Background(HeadFill).BorderBottom(0.75f).BorderColor(Line).Padding(4)
                                .Text(head).FontSize(8).SemiBold().FontColor(Muted);
                    });
                    foreach (string[] row in rows)
                    {
                        for (int i = 0; i < headers.Length; i++)
                        {
                            string cell = i < row.Length ? row[i] : "";
                            table.Cell().BorderBottom(0.5f).BorderColor(Line).Padding(4).Text(cell).FontSize(8.5f).FontColor(Ink);
                        }
                    }
                });
            });
        }
    }
}
