using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Documents;
using QuestPDF.Fluent;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] brute-force sweep for "Composed layout generates infinite document" on the payslip.
    /// Renders FichePaieDocument over a matrix of extreme inputs (no database) and records every case
    /// that throws, to docs/payslip-layout/sweep-&lt;tag&gt;.txt. Built only on members that exist in
    /// 1.43.1 too (the 1.44.0 flag is set by reflection when present), so the SAME file runs on older
    /// commits for the version bisect.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class PayslipLayoutSweep
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

        // ---- the case description ------------------------------------------------------------
        public sealed class Case
        {
            public string Name;
            public bool Arabic, LegalOn = true, Cacobatph;
            public int Lines = 4;
            public string LineLabel = "Prime de rendement";
            public decimal Amount = 8000m;
            public string LastName = "BENALI", FirstName = "Karim", Company = "SARL Atlas Industrie",
                          Address = "Zone industrielle, Rouiba — Alger", Poste = "Technicien", Category = "Agent",
                          Rib = "00799999000123456789", Nss = "000000000000";
            public int LogoW, LogoH; // 0 = no logo
            public override string ToString() => Name;
        }

        public static FichePaieModel Model(Case k)
        {
            var company = new Company
            {
                Id = 1, NameFr = k.Company, NameAr = k.Company, AddressFr = k.Address, AddressAr = k.Address,
                BtphSector = k.Cacobatph, CacobatphEnabled = k.Cacobatph,
                Logo = k.LogoW > 0 ? Png(k.LogoW, k.LogoH) : null
            };
            var employee = new Employee
            {
                Id = 7, LastNameFr = k.LastName, FirstNameFr = k.FirstName, LastNameAr = k.LastName, FirstNameAr = k.FirstName,
                Poste = k.Poste, Category = k.Category, Rib = k.Rib, Nss = k.Nss,
                HireDate = new DateTime(2022, 3, 1), MaritalStatus = MaritalStatus.Married
            };
            decimal gross = 60000m;
            var m = new FichePaieModel
            {
                Company = company, Employee = employee, Year = 2026, Month = 10, IsArabic = k.Arabic,
                SalaireBrut = gross, BaseCotisable = gross, CnasEmployee = 5400m, CnasEmployeeRate = 0.09m,
                BaseImposable = 54600m, IrgBrut = 8542m, Abattement = 1500m, Irg = 7042m, NetSalaire = 47558m, WorkedDays = 30m
            };
            m.Lines.Add(new FicheLineModel { Code = "001", Label = "Salaire de base", BaseText = Money(gross), TauxText = "", Gain = gross });
            for (int i = 0; i < k.Lines; i++)
            {
                bool gain = i % 2 == 0;
                m.Lines.Add(new FicheLineModel
                {
                    Code = (i + 2).ToString("000"), Label = k.LineLabel + (k.Lines > 1 ? " " + (i + 1) : ""),
                    BaseText = Money(k.Amount), TauxText = "",
                    Gain = gain ? k.Amount : (decimal?)null, Retenue = gain ? (decimal?)null : k.Amount
                });
            }
            PropertyInfo flag = typeof(FichePaieModel).GetProperty("LegalDeductionsApplied");
            if (flag != null) flag.SetValue(m, k.LegalOn); // 1.44.0+: the IRG/CNAS switch
            return m;
        }

        public static byte[] Png(int w, int h)
        {
            using (var bmp = new SkiaSharp.SKBitmap(w, h))
            {
                bmp.Erase(new SkiaSharp.SKColor(0x0B, 0x52, 0x30));
                using (var img = SkiaSharp.SKImage.FromBitmap(bmp))
                using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90)) return data.ToArray();
            }
        }

        private static string Money(decimal v) => v.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

        public static string Render(Case k, out long ms)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                byte[] pdf = Document.Create(c => new FichePaieDocument(Model(k)).Compose(c)).GeneratePdf();
                ms = sw.ElapsedMilliseconds;
                return "OK (" + pdf.Length + " bytes)";
            }
            catch (Exception ex)
            {
                ms = sw.ElapsedMilliseconds;
                Exception e = ex; while (e.InnerException != null) e = e.InnerException;
                return "FAIL " + e.GetType().Name + ": " + (e.Message ?? "").Split('\n')[0];
            }
        }

        // ---- the matrix ----------------------------------------------------------------------
        public static IEnumerable<Case> Matrix()
        {
            string frWords = string.Join(" ", Enumerable.Repeat("Indemnité forfaitaire de déplacement et de représentation", 6));
            string arWords = string.Join(" ", Enumerable.Repeat("تعويض جزافي عن التنقل والتمثيل", 8));
            Func<int, string> frRun = n => new string('W', n);                       // one word, no spaces (wide glyph)
            Func<int, string> frRunMix = n => string.Concat(Enumerable.Repeat("Indemnité", n / 9 + 1)).Substring(0, n);
            Func<int, string> arRun = n => string.Concat(Enumerable.Repeat("تعويضجزافيعنالتنقل", n / 18 + 1)).Substring(0, n);

            yield return new Case { Name = "baseline FR" };
            yield return new Case { Name = "baseline AR", Arabic = true };

            foreach (bool ar in new[] { false, true })
            foreach (bool legal in new[] { true, false })
            foreach (var (w, h) in new[] { (200, 200), (600, 120), (100, 101), (1000, 1001), (100, 110), (100, 150), (100, 200), (120, 600), (60, 2000), (1, 1), (4000, 4000), (3000, 3100) })
                yield return new Case { Name = (ar ? "AR" : "FR") + (legal ? " legalON" : " legalOFF") + " | logo " + w + "x" + h, Arabic = ar, LegalOn = legal, LogoW = w, LogoH = h };

            foreach (bool ar in new[] { false, true })
            foreach (bool legal in new[] { true, false })
            foreach (bool caco in new[] { false, true })
            {
                string tag = (ar ? "AR" : "FR") + (legal ? " legalON" : " legalOFF") + (caco ? " caco" : "");
                yield return new Case { Name = tag + " | long FR label w/ spaces", Arabic = ar, LegalOn = legal, Cacobatph = caco, LineLabel = frWords };
                yield return new Case { Name = tag + " | long AR label w/ spaces", Arabic = ar, LegalOn = legal, Cacobatph = caco, LineLabel = arWords };
                foreach (int n in new[] { 20, 40, 60, 90, 150, 300 })
                {
                    yield return new Case { Name = tag + " | FR no-space label W×" + n, Arabic = ar, LegalOn = legal, Cacobatph = caco, LineLabel = frRun(n) };
                    yield return new Case { Name = tag + " | FR no-space label mixed×" + n, Arabic = ar, LegalOn = legal, Cacobatph = caco, LineLabel = frRunMix(n) };
                    yield return new Case { Name = tag + " | AR no-space label ×" + n, Arabic = ar, LegalOn = legal, Cacobatph = caco, LineLabel = arRun(n) };
                }
                foreach (int lines in new[] { 10, 20, 40, 60 })
                    yield return new Case { Name = tag + " | " + lines + " lines", Arabic = ar, LegalOn = legal, Cacobatph = caco, Lines = lines };
                foreach (decimal amt in new[] { 0m, -1500m, 9999999.99m, 123456789.12m, 98765432109.87m, 9999999999999.99m })
                    yield return new Case { Name = tag + " | amount " + amt, Arabic = ar, LegalOn = legal, Cacobatph = caco, Amount = amt };
                foreach (int n in new[] { 40, 80, 150 })
                {
                    yield return new Case { Name = tag + " | no-space last name ×" + n, Arabic = ar, LegalOn = legal, Cacobatph = caco, LastName = ar ? arRun(n) : frRun(n) };
                    yield return new Case { Name = tag + " | no-space company ×" + n, Arabic = ar, LegalOn = legal, Cacobatph = caco, Company = ar ? arRun(n) : frRun(n) };
                    yield return new Case { Name = tag + " | no-space address ×" + n, Arabic = ar, LegalOn = legal, Cacobatph = caco, Address = ar ? arRun(n) : frRun(n) };
                    yield return new Case { Name = tag + " | no-space poste ×" + n, Arabic = ar, LegalOn = legal, Cacobatph = caco, Poste = frRun(n) };
                    yield return new Case { Name = tag + " | no-space RIB ×" + n, Arabic = ar, LegalOn = legal, Cacobatph = caco, Rib = new string('9', n) };
                }
                yield return new Case { Name = tag + " | long names w/ spaces", Arabic = ar, LegalOn = legal, Cacobatph = caco,
                    LastName = ar ? arWords : frWords, Company = ar ? arWords : frWords, Address = ar ? arWords : frWords, Poste = frWords, Category = frWords };
                yield return new Case { Name = tag + " | 60 lines + AR no-space label ×90 + huge amount", Arabic = ar, LegalOn = legal, Cacobatph = caco,
                    Lines = 60, LineLabel = arRun(90), Amount = 98765432109.87m };
            }
        }

        [Test]
        public void Sweep()
        {
            string tag = Environment.GetEnvironmentVariable("SWEEP_TAG");
            if (string.IsNullOrWhiteSpace(tag)) tag = "run";
            var sb = new StringBuilder();
            int total = 0, fails = 0;
            string filter = Environment.GetEnvironmentVariable("SWEEP_FILTER");
            foreach (Case k in Matrix())
            {
                if (!string.IsNullOrEmpty(filter) && k.Name.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                total++;
                string r = Render(k, out long ms);
                if (r.StartsWith("FAIL")) fails++;
                sb.AppendLine((r.StartsWith("FAIL") ? "FAIL | " : "ok   | ") + k.Name + " | " + ms + " ms | " + r);
            }
            sb.Insert(0, "sweep " + tag + ": " + total + " cases, " + fails + " FAIL\n");
            string dir = Path.Combine(FindRepoRoot(), "docs", "payslip-layout");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "sweep-" + tag + ".txt"), sb.ToString(), new UTF8Encoding(false));
            TestContext.Out.WriteLine("sweep " + tag + ": " + total + " cases, " + fails + " FAIL");
        }

        [Test]
        public void Write_Minimal_Case_Pdfs()
        {
            string dir = Path.Combine(FindRepoRoot(), "docs", "payslip-layout");
            Directory.CreateDirectory(dir);
            foreach (var (name, k) in new[]
            {
                ("minimal-logo-100x101-FR", new Case { Name = "m", LogoW = 100, LogoH = 101 }),
                ("portrait-logo-300x600-FR", new Case { Name = "p", LogoW = 300, LogoH = 600 }),
                ("portrait-logo-300x600-AR", new Case { Name = "p", LogoW = 300, LogoH = 600, Arabic = true, LastName = "بن علي", FirstName = "كريم", Company = "شركة أطلس للصناعة" }),
            })
            {
                FichePaieModel m = Model(k);
                Document.Create(c => new FichePaieDocument(m).Compose(c)).GeneratePdf(Path.Combine(dir, name + ".pdf"));
                Document.Create(c => new FichePaieDocument(m).Compose(c))
                    .WithMetadata(new QuestPDF.Drawing.DocumentMetadata { RasterDpi = 110 })
                    .GenerateImages(i => Path.Combine(dir, name + (i == 0 ? "" : "-" + i) + ".png"));
            }
        }

        private static string FindRepoRoot()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OptiPaie.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
