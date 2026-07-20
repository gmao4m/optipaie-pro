using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>Builds the Fiche de Paie model and produces preview / PDF / print output.</summary>
    public sealed class FicheService
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public FichePaieModel FromResult(Company company, Employee employee, int year, int month, PayrollResult result, bool isArabic, decimal workedDays)
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
                BaseImposable = t.BaseImposable,
                IrgBrut = t.IrgBrut,
                Abattement = t.Abattement,
                Irg = t.Irg,
                Lissage = LissageFromTrace(result),
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
                    Label = isArabic && !string.IsNullOrWhiteSpace(line.LabelAr) ? line.LabelAr : line.LabelFr,
                    BaseText = BaseText(line.Base, line.Quantity, line.Amount),
                    TauxText = TauxText(line.Rate, line.UnitPrice),
                    Gain = gain ? line.Amount : (decimal?)null,
                    Retenue = gain ? (decimal?)null : line.Amount
                });
                i++;
            }

            return model;
        }

        public FichePaieModel FromPayslip(Company company, Employee employee, Payslip payslip, bool isArabic, int year, int month)
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
                BaseImposable = payslip.BaseImposable,
                IrgBrut = payslip.IrgBrut,
                Abattement = payslip.Abattement,
                Irg = payslip.Irg,
                NetSalaire = payslip.NetSalaire,
                WorkedDays = payslip.WorkedDays
            };

            int i = 1;
            foreach (PayrollDetail line in payslip.Details)
            {
                bool gain = line.ElementType == ElementType.Gain;
                model.Lines.Add(new FicheLineModel
                {
                    Code = i.ToString("000", Fr),
                    Label = isArabic && !string.IsNullOrWhiteSpace(line.LabelAr) ? line.LabelAr : line.LabelFr,
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
            string path = WriteTemp(model);
            Open(path, null);
        }

        public void Print(FichePaieModel model)
        {
            string path = WriteTemp(model);
            Open(path, "print");
        }

        public void ExportPdf(FichePaieModel model, string path)
        {
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
