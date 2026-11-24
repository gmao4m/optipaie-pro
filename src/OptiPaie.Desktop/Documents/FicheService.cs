using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Security;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Builds the Fiche de Paie model and produces preview / PDF / print output.</summary>
    public sealed class FicheService
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>
        /// The single chokepoint for every payslip PDF (archive reprint, payroll screen, employee
        /// profile, batch). The authorization context is set by the hosting view model so that
        /// VIEWING the fiche is gated as the payroll screen is, and PRINTING / EXPORTING it is gated
        /// exactly as strictly as viewing payslip data — HR may view payroll but never export it, and
        /// read-only sees no payslip at all. Null / gate off ⇒ unrestricted (legacy behaviour).
        /// </summary>
        public IAccessContext Access { get; set; }

        private void RequireView()
        {
            if (Access != null && Access.IsEnforced && !AccessPolicy.CanOpen(Access.CurrentRole, "payroll"))
            {
                throw new AccessDeniedException(Permission.RunPayroll);
            }
        }

        private void RequireExport()
        {
            if (Access != null && Access.IsEnforced && !AccessPolicy.CanExportPayroll(Access.CurrentRole))
            {
                throw new AccessDeniedException(Permission.RunPayroll);
            }
        }

        public FichePaieModel FromResult(Company company, Employee employee, int year, int month, PayrollResult result, bool isArabic, decimal workedDays, decimal cnasEmployeeRate)
        {
            PayrollTotals t = result.Totals;
            var model = new FichePaieModel
            {
                Company = company,
                Employee = employee,
                Year = year,
                Month = month,
                IsArabic = isArabic,
                SalaireBrut = t.SalaireBrut,
                BaseCotisable = t.BaseCotisable,
                CnasEmployee = t.CnasEmployee,
                CnasEmployeeRate = cnasEmployeeRate,
                BaseImposable = t.BaseImposable,
                IrgBrut = t.IrgBrut,
                Abattement = t.Abattement,
                Irg = t.Irg,
                Lissage = LissageFromTrace(result),
                LegalDeductionsApplied = result.LegalDeductionsApplied,
                NetSalaire = t.NetSalaire,
                WorkedDays = workedDays
            };

            int i = 1;
            foreach (PayrollLineResult line in result.Lines)
            {
                bool gain = line.ElementType == ElementType.Gain;
                model.Lines.Add(new FicheLineModel
                {
                    Code = i.ToString("000", Fr),
                    Label = LineLabel(line.LabelFr, line.LabelAr, isArabic),
                    BaseText = BaseText(line.Base, line.Quantity, line.Amount),
                    TauxText = TauxText(line.Rate, line.UnitPrice),
                    Gain = gain ? line.Amount : (decimal?)null,
                    Retenue = gain ? (decimal?)null : line.Amount
                });
                i++;
            }

            return model;
        }

        public FichePaieModel FromPayslip(Company company, Employee employee, Payslip payslip, bool isArabic, int year, int month, string revisionMark = null)
        {
            var model = new FichePaieModel
            {
                Company = company,
                Employee = employee,
                Year = year,
                Month = month,
                IsArabic = isArabic,
                SalaireBrut = payslip.SalaireBrut,
                BaseCotisable = payslip.BaseCotisable,
                CnasEmployee = payslip.CnasEmployee,
                CnasEmployeeRate = payslip.CnasEmployeeRateUsed,
                BaseImposable = payslip.BaseImposable,
                IrgBrut = payslip.IrgBrut,
                Abattement = payslip.Abattement,
                Irg = payslip.Irg,
                NetSalaire = payslip.NetSalaire,
                WorkedDays = payslip.WorkedDays,
                // A reprint ALWAYS uses the flag frozen on THIS payslip, never the current setting.
                LegalDeductionsApplied = payslip.LegalDeductionsApplied,
                // A corrected version (it supersedes an earlier payslip) prints the rectificatif badge.
                RevisionMark = payslip.SupersedesPayslipId.HasValue ? revisionMark : null
            };

            int i = 1;
            foreach (PayrollDetail line in payslip.Details)
            {
                bool gain = line.ElementType == ElementType.Gain;
                model.Lines.Add(new FicheLineModel
                {
                    Code = i.ToString("000", Fr),
                    Label = LineLabel(line.LabelFr, line.LabelAr, isArabic),
                    BaseText = BaseText(line.Base, line.Quantity, line.Amount),
                    TauxText = TauxText(line.Rate, line.UnitPrice),
                    Gain = gain ? line.Amount : (decimal?)null,
                    Retenue = gain ? (decimal?)null : line.Amount
                });
                i++;
            }

            return model;
        }

        public void Preview(FichePaieModel model)
        {
            RequireView(); // viewing a payslip is gated as the payroll screen is (not read-only)
            string path = WriteTemp(model);
            Open(path, null);
        }

        public void Print(FichePaieModel model)
        {
            RequireExport(); // printing a payslip is gated exactly as strictly as viewing it
            string path = WriteTemp(model);
            Open(path, "print");
        }

        public void ExportPdf(FichePaieModel model, string path)
        {
            RequireExport(); // exporting a payslip PDF is gated exactly as strictly as viewing it
            Render(model, path);
        }

        private static string WriteTemp(FichePaieModel model)
        {
            string path = Path.Combine(Path.GetTempPath(), "fiche_paie_" + Guid.NewGuid().ToString("N") + ".pdf");
            Render(model, path);
            return path;
        }

        private static void Render(FichePaieModel model, string path)
        {
            Document.Create(container => new FichePaieDocument(model).Compose(container)).GeneratePdf(path);
        }

        private static void Open(string path, string verb)
        {
            var info = new ProcessStartInfo(path) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(verb))
            {
                info.Verb = verb;
            }

            Process.Start(info);
        }

        private static decimal LissageFromTrace(PayrollResult result)
        {
            foreach (PayrollCalculationStep step in result.Trace)
            {
                if (step.Key == "LISSAGE")
                {
                    return step.Amount;
                }
            }

            return 0m;
        }

        /// <summary>Resolves a payslip line label for the chosen language. Prefers a genuine Arabic
        /// label; otherwise translates the SYSTEM-generated labels (loan recovery…) so an Arabic
        /// payslip is not left with a lone French line. User-named rubrics keep their own text.</summary>
        private static string LineLabel(string labelFr, string labelAr, bool isArabic)
        {
            if (!isArabic) return labelFr;
            string fr = (labelFr ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(labelAr) && labelAr.Trim() != fr) return labelAr;
            switch (fr)
            {
                case "Remboursement prêt": return "استرجاع قرض";
                case "Salaire de base": return "الأجر القاعدي";
                default: return labelFr;
            }
        }

        private static string BaseText(decimal? @base, decimal? quantity, decimal amount)
        {
            if (@base.HasValue) return @base.Value.ToString("N2", Fr);
            if (quantity.HasValue) return quantity.Value.ToString("0.##", Fr);
            return amount.ToString("N2", Fr);
        }

        private static string TauxText(decimal? rate, decimal? unitPrice)
        {
            if (rate.HasValue) return (rate.Value * 100m).ToString("0.##", Fr) + " %";
            if (unitPrice.HasValue) return unitPrice.Value.ToString("N2", Fr);
            return string.Empty;
        }
    }
}
