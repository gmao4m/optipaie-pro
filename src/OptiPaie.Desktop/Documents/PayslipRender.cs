using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OptiPaie.Core.Entities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>
    /// A payslip that could not be produced. Carries WHICH employee/period failed and a plain bilingual
    /// message for the accountant; the original exception is kept as <see cref="Exception.InnerException"/>
    /// and logged in full — it is never shown raw to the user.
    /// </summary>
    public sealed class PayslipRenderException : Exception
    {
        public PayslipRenderException(string employee, string period, Exception inner)
            : base(BuildMessage(employee, period), inner)
        {
            Employee = employee;
            Period = period;
        }

        public string Employee { get; }
        public string Period { get; }

        private static string BuildMessage(string employee, string period) =>
            "La fiche de paie de " + employee + " (" + period + ") n'a pas pu être produite. " +
            "Les détails techniques ont été enregistrés : transmettez-les au support. Les autres fiches ne sont pas concernées." +
            Environment.NewLine + Environment.NewLine +
            "تعذّر إنتاج كشف راتب " + employee + " (" + period + "). " +
            "سُجّلت التفاصيل التقنية: أرسلها إلى الدعم. الكشوف الأخرى غير معنيّة.";
    }

    /// <summary>Guarded payslip rendering: single payslips and whole batches.</summary>
    public static class PayslipRender
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>The employee name and period used to name a failed payslip.</summary>
        public static string EmployeeOf(FichePaieModel m)
        {
            Employee e = m?.Employee;
            if (e == null) return "—";
            string name = (e.LastNameFr + " " + e.FirstNameFr).Trim();
            if (string.IsNullOrWhiteSpace(name)) name = (e.LastNameAr + " " + e.FirstNameAr).Trim();
            return string.IsNullOrWhiteSpace(name) ? "#" + e.Id : name;
        }

        public static string PeriodOf(FichePaieModel m) =>
            m == null || m.Month < 1 || m.Month > 12 ? "—" : m.Month.ToString("00", Fr) + "/" + m.Year;

        /// <summary>Wraps any failure while producing <paramref name="m"/>'s payslip, naming the employee.</summary>
        public static PayslipRenderException Wrap(FichePaieModel m, Exception ex) =>
            ex as PayslipRenderException ?? new PayslipRenderException(EmployeeOf(m), PeriodOf(m), ex);

        /// <summary>The default composition of one payslip into a document container.</summary>
        public static void ComposeOne(FichePaieModel m, IDocumentContainer container) => new FichePaieDocument(m).Compose(container);

        /// <summary>Result of a batch export: how many payslips were written, and which could not be produced.</summary>
        public sealed class BatchResult
        {
            public int Exported { get; set; }
            public List<PayslipRenderException> Failed { get; } = new List<PayslipRenderException>();
        }

        /// <summary>
        /// Exports every payslip into ONE PDF. Normal case: a single pass, exactly as before (same output).
        /// If that pass fails, each payslip is tried on its own; the ones that render are exported together
        /// and each one that cannot be produced is reported by name — one bad payslip never kills the batch.
        /// <paramref name="compose"/> is replaceable for tests; it defaults to the real payslip document.
        /// </summary>
        public static BatchResult ExportBatch(IReadOnlyList<FichePaieModel> models, string path,
            Action<FichePaieModel, IDocumentContainer> compose = null)
        {
            compose = compose ?? ComposeOne;
            var result = new BatchResult();
            try
            {
                Document.Create(c => { foreach (FichePaieModel m in models) compose(m, c); }).GeneratePdf(path);
                result.Exported = models.Count;
                return result;
            }
            catch (Exception)
            {
                // Isolate the culprit(s): render each payslip alone, keep the good ones.
            }

            var good = new List<FichePaieModel>();
            foreach (FichePaieModel m in models)
            {
                try
                {
                    Document.Create(c => compose(m, c)).GeneratePdf(Stream.Null);
                    good.Add(m);
                }
                catch (Exception ex)
                {
                    result.Failed.Add(Wrap(m, ex));
                }
            }

            if (good.Count > 0)
                Document.Create(c => { foreach (FichePaieModel m in good) compose(m, c); }).GeneratePdf(path);
            result.Exported = good.Count;
            return result;
        }
    }
}
