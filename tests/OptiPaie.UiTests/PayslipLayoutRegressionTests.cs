using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using OptiPaie.Desktop.Documents;
using QuestPDF.Fluent;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Production bug (since 1.30.3): "Composed layout generates infinite document" when printing a payslip.
    /// Cause: the header's fixed 50×50 logo box scaled the logo to its WIDTH, so any logo even 1 px taller
    /// than wide needed more than 50 pt of height and could never fit. These tests keep it fixed:
    /// a matrix of extreme payslips must all render, and a payslip that genuinely cannot be produced is
    /// reported by name — it never kills a batch and never reaches the accountant raw.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public sealed class PayslipLayoutRegressionTests
    {
        [OneTimeSetUp]
        public void Fonts()
        {
            if (Application.Current == null)
            {
                var app = new OptiPaie.Desktop.App();
                typeof(OptiPaie.Desktop.App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            typeof(OptiPaie.Desktop.App).Assembly.GetType("OptiPaie.Desktop.Documents.PdfFonts")
                .GetMethod("Register", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        }

        [Test]
        public void Every_Extreme_Payslip_Renders_Without_Throwing()
        {
            var failures = new List<string>();
            int count = 0;
            foreach (PayslipLayoutSweep.Case k in PayslipLayoutSweep.Matrix())
            {
                count++;
                string r = PayslipLayoutSweep.Render(k, out long ms);
                if (r.StartsWith("FAIL")) failures.Add(k.Name + " → " + r);
            }

            TestContext.Out.WriteLine(count + " extreme payslips rendered, " + failures.Count + " failed");
            Assert.That(count, Is.GreaterThan(400), "the matrix is the guard: keep it wide");
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void The_Minimal_Production_Case_A_Portrait_Logo_Renders()
        {
            // The exact production trigger: a logo 1 px taller than it is wide.
            var k = new PayslipLayoutSweep.Case { Name = "logo 100x101", LogoW = 100, LogoH = 101 };
            Assert.That(PayslipLayoutSweep.Render(k, out _), Does.StartWith("OK"));
        }

        [Test]
        public void A_Batch_With_One_Broken_Payslip_Exports_The_Others_And_Names_It()
        {
            var models = new List<FichePaieModel>();
            foreach (var (last, first) in new[] { ("AMRANI", "Salima"), ("BENALI", "Karim"), ("ZEROUAL", "Nadia"), ("HADDAD", "Bilal") })
            {
                FichePaieModel m = PayslipLayoutSweep.Model(new PayslipLayoutSweep.Case { Name = last, LastName = last, FirstName = first });
                models.Add(m);
            }

            // Deliberately broken payslip: ZEROUAL's page demands a fixed height no page can hold — a REAL
            // QuestPDF "infinite document" layout failure, not a fake exception.
            Action<FichePaieModel, QuestPDF.Infrastructure.IDocumentContainer> compose = (m, c) =>
            {
                if (m.Employee.LastNameFr == "ZEROUAL")
                    c.Page(p => p.Content().Height(5000).Text("x"));
                else
                    new FichePaieDocument(m).Compose(c);
            };

            string path = Path.Combine(Path.GetTempPath(), "batch-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                PayslipRender.BatchResult result = PayslipRender.ExportBatch(models, path, compose);

                Assert.That(result.Exported, Is.EqualTo(3), "the three good payslips are exported");
                Assert.That(result.Failed.Count, Is.EqualTo(1));
                Assert.That(result.Failed[0].Employee, Is.EqualTo("ZEROUAL Nadia"), "the message names the right employee");
                Assert.That(result.Failed[0].Period, Is.EqualTo("10/2026"));
                Assert.That(result.Failed[0].Message, Does.Contain("ZEROUAL Nadia").And.Contain("تعذّر"), "bilingual, names the employee");
                Assert.That(result.Failed[0].InnerException, Is.Not.Null, "the original exception is kept for the log");
                Assert.That(new FileInfo(path).Length, Is.GreaterThan(10000), "the PDF with the good payslips was written");
                TestContext.Out.WriteLine("batch: exported " + result.Exported + ", failed: " + result.Failed[0].Employee +
                                          " — inner " + result.Failed[0].InnerException.GetType().Name);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [Test]
        public void A_Single_Payslip_Failure_Surfaces_As_A_Named_Friendly_Exception()
        {
            FichePaieModel m = PayslipLayoutSweep.Model(new PayslipLayoutSweep.Case { Name = "x", LastName = "HADDAD", FirstName = "Bilal" });
            // A genuine failure inside the real FicheService render path: the target folder does not exist.
            string impossible = Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"), "fiche.pdf");

            var ex = Assert.Throws<PayslipRenderException>(() => new FicheService().ExportPdf(m, impossible));
            Assert.That(ex.Employee, Is.EqualTo("HADDAD Bilal"));
            Assert.That(ex.Message, Does.Contain("HADDAD Bilal").And.Contain("10/2026").And.Contain("تعذّر"));
            Assert.That(ex.InnerException, Is.Not.Null);
        }

        [Test]
        public void A_Healthy_Batch_Is_Exported_In_One_Pass_With_No_Failures()
        {
            var models = Enumerable.Range(1, 3).Select(i =>
                PayslipLayoutSweep.Model(new PayslipLayoutSweep.Case { Name = "n" + i, LastName = "EMP" + i })).ToList();
            string path = Path.Combine(Path.GetTempPath(), "batch-ok-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                PayslipRender.BatchResult r = PayslipRender.ExportBatch(models, path);
                Assert.That(r.Exported, Is.EqualTo(3));
                Assert.That(r.Failed, Is.Empty);
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
