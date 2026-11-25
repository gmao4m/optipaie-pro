using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using QuestPDF.Drawing;
using QuestPDF.Fluent;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] PDF identity proof for the payslip layout fix. Renders, with pinned PDF timestamps:
    ///   - the REAL payslips of a COPY of the live database (never the live file), FR and AR, through
    ///     the exact reprint path (FicheService.FromPayslip → FichePaieDocument);
    ///   - normal-data payslips with a square logo, a landscape logo and no logo.
    /// Writes one SHA-256 per PDF to docs/payslip-layout/pdf-&lt;tag&gt;.txt. Run before and after the fix:
    /// the two files must be identical (normal data must render exactly as before).
    ///   env PDF_TAG = before|after, optional LEGAL_PROOF_DB = a frozen snapshot of the live database.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class PayslipPdfIdentityHarness
    {
        private static readonly DateTime Pinned = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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

        private static byte[] Pdf(FichePaieModel m) =>
            Document.Create(c => new FichePaieDocument(m).Compose(c))
                .WithMetadata(new DocumentMetadata { CreationDate = Pinned, ModifiedDate = Pinned })
                .GeneratePdf();

        private static string Sha(byte[] b)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }

        [Test]
        public void Render_Real_And_Normal_Payslips()
        {
            string tag = Environment.GetEnvironmentVariable("PDF_TAG");
            if (string.IsNullOrWhiteSpace(tag)) tag = "run";
            string src = Environment.GetEnvironmentVariable("LEGAL_PROOF_DB");
            if (string.IsNullOrWhiteSpace(src))
                src = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OptiPaie DZ", AppConstants.DatabaseFileName);

            string dir = Path.Combine(Path.GetTempPath(), "optipaie-pdfid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.Copy(src, Path.Combine(dir, AppConstants.DatabaseFileName)); // COPY — the live file is only read
            var lines = new List<string>();
            var fiche = new FicheService();
            try
            {
                AppServices services = CompositionRoot.Build(new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr"));
                var ids = new List<(long Id, long Emp, long Company, int Y, int M)>();
                using (var c = new SQLiteConnection("Data Source=" + Path.Combine(dir, AppConstants.DatabaseFileName) + ";Version=3;"))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "SELECT p.Id, p.EmployeeId, r.CompanyId, r.PeriodYear, r.PeriodMonth FROM Payslips p " +
                                          "JOIN PayrollRuns r ON r.Id = p.RunId WHERE p.Status = 1 ORDER BY p.Id;";
                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read())
                                ids.Add((Convert.ToInt64(rd[0]), Convert.ToInt64(rd[1]), Convert.ToInt64(rd[2]), Convert.ToInt32(rd[3]), Convert.ToInt32(rd[4])));
                    }
                }

                foreach (var p in ids)
                {
                    Payslip ps = services.Archive.GetPayslip(p.Id);
                    Company co = services.Companies.Get(p.Company);
                    Employee em = services.Employees.Get(p.Emp);
                    foreach (bool ar in new[] { false, true })
                        lines.Add("real #" + p.Id + (ar ? " AR" : " FR") + " logo=" + (co?.Logo == null ? "none" : co.Logo.Length + "B") +
                                  " | " + Sha(Pdf(fiche.FromPayslip(co, em, ps, ar, p.Y, p.M))));
                }

                // Normal data with every logo shape that rendered BEFORE the fix (square / landscape / none).
                foreach (bool ar in new[] { false, true })
                foreach (bool legal in new[] { true, false })
                foreach (var (w, h) in new[] { (0, 0), (200, 200), (600, 120), (4000, 4000), (1, 1) })
                {
                    var k = new PayslipLayoutSweep.Case { Name = "n", Arabic = ar, LegalOn = legal, LogoW = w, LogoH = h };
                    lines.Add("normal " + (ar ? "AR" : "FR") + (legal ? " ON" : " OFF") + " logo " + (w == 0 ? "none" : w + "x" + h) +
                              " | " + Sha(Pdf(PayslipLayoutSweep.Model(k))));
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }

            string outDir = Path.Combine(FindRepoRoot(), "docs", "payslip-layout");
            Directory.CreateDirectory(outDir);
            string body = string.Join("\n", lines) + "\n";
            File.WriteAllText(Path.Combine(outDir, "pdf-" + tag + ".txt"), body, new UTF8Encoding(false));
            TestContext.Out.WriteLine("pdfs=" + lines.Count + " overall=" + Sha(Encoding.UTF8.GetBytes(body)));
        }

        private static string FindRepoRoot()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OptiPaie.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
