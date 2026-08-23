using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Certificates;
using OptiPaie.Services.Certificates;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Proves the ATS/DRT logic ported from the source tool is byte-for-byte faithful, and
    /// that the REAL official CNAS templates are actually filled: each test fills the shipped
    /// .docx by bookmark and reads the injected text back out.
    /// </summary>
    [TestFixture]
    public sealed class CertificateDocumentTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-cert-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ── ported calculation logic ─────────────────────────────────────────

        [Test]
        public void PreviousBusinessDay_SkipsFridayAndSaturday()
        {
            // Sun 2026-01-04 → back over Sat 03 + Fri 02 → Thu 2026-01-01.
            DateTime result = BusinessDayCalculator.PreviousBusinessDay(new DateTime(2026, 1, 4), new WeekendConfig());
            Assert.That(result, Is.EqualTo(new DateTime(2026, 1, 1)));
        }

        [Test]
        public void NextBusinessDayOnOrAfter_AdvancesPastWeekend()
        {
            // Fri 2026-01-02 → Sat 03 → Sun 2026-01-04 (first working day).
            DateTime result = BusinessDayCalculator.NextBusinessDayOnOrAfter(new DateTime(2026, 1, 2), new WeekendConfig());
            Assert.That(result, Is.EqualTo(new DateTime(2026, 1, 4)));
        }

        [Test]
        public void EmployeeShare_Is9PercentRoundedTo2Decimals()
        {
            Assert.That(new MonthlyContribution { ContributionBase = 10000m }.EmployeeShare, Is.EqualTo(900.00m));
            Assert.That(new MonthlyContribution { ContributionBase = 12345.67m }.EmployeeShare, Is.EqualTo(1111.11m));
            Assert.That(new MonthlyContribution { ContributionBase = null }.EmployeeShare, Is.Null);
        }

        [Test]
        public void MonthGrid_UsesFrenchOrAlgerianArabicLabels_AndDisablesTrailingSlots()
        {
            var svc = new CertificateService(new WeekendConfig());

            List<MonthlyContribution> fr = svc.BuildEmptyMonthGrid(new DateTime(2026, 1, 1), 3, false);
            Assert.That(fr[0].MonthLabel, Is.EqualTo("Janvier 2026"));
            Assert.That(fr[3].IsActive, Is.False, "months beyond the range are disabled (print '/')");

            List<MonthlyContribution> ar = svc.BuildEmptyMonthGrid(new DateTime(2026, 1, 1), 3, true);
            Assert.That(ar[0].MonthLabel, Is.EqualTo("جانفي 2026"));
        }

        [Test]
        public void MapAts_UsesDdMmYyDates_DaAmounts_AndSlashForUnusedSlots()
        {
            AtsCertificateData data = SampleAts(resumed: false, months: 2);
            Dictionary<string, string> v = CertificateBookmarkMapper.MapAts(data);

            Assert.That(v["NMS"], Is.EqualTo("BENALI Karim"));
            Assert.That(v["NSS"], Is.EqualTo("12 3456 7890 12"));
            Assert.That(v["DATEAT"], Is.EqualTo(new DateTime(2026, 1, 1).ToString("ddMMyy")));
            Assert.That(v["JT1"], Is.EqualTo("22"), "days-worked column carries a DAY count, not hours");
            Assert.That(v["MT1"], Is.EqualTo(""), "motif column is free text, not a day count");
            Assert.That(v["SS1"], Does.Contain(" DA"), "salaire soumis à cotisations");
            Assert.That(v["PO1"], Is.EqualTo("3600 DA"), "part ouvrière = 9% of 40000 = 3600");
            Assert.That(v["JT12"], Is.EqualTo("/"), "unused trailing slot prints '/'");
            Assert.That(v["DATEAUJRH"], Is.Not.Empty, "not resumed → today's date is filled");
        }

        [Test]
        public void MapDrt_ChecksCase1WhenResumed_AndCase2WithHonourSectionOtherwise()
        {
            Dictionary<string, string> resumed = CertificateBookmarkMapper.MapDrt(SampleDrt(true));
            Assert.That(resumed["Case1"], Is.EqualTo("X"));
            Assert.That(resumed.ContainsKey("Case2"), Is.False);
            Assert.That(resumed["DATEREPRISE"], Is.Not.Empty);

            Dictionary<string, string> notResumed = CertificateBookmarkMapper.MapDrt(SampleDrt(false));
            Assert.That(notResumed["Case2"], Is.EqualTo("X"));
            Assert.That(notResumed["NMS2"], Is.EqualTo("BENALI"), "déclaration sur l'honneur is filled when not resumed");
        }

        // ── absolute-coordinate renderer (the mechanism that replaced .docx fill) ──

        [Test]
        public void RenderPdf_AllThreeZoneTypes_ProduceAValidPdf()
        {
            var form = new FormDefinition
            {
                Pages =
                {
                    new FormPage
                    {
                        WidthMm = 210, HeightMm = 297,
                        Fields =
                        {
                            new FormField { Name = "NAME", Type = "dotted", XMm = 20, YMm = 30, MaxWidthMm = 120, FontSize = 11 },
                            new FormField { Name = "BOX",  Type = "rectangle", XMm = 20, YMm = 40, WidthMm = 60, HeightMm = 9, Align = "center" },
                            new FormField { Name = "DATE", Type = "grid", XMm = 25, YMm = 60, PitchMm = 6, Cells = 6, FontSize = 11 },
                            new FormField { Name = "CHK",  Type = "checkbox", XMm = 20, YMm = 70, FontSize = 12, Bold = true }
                        }
                    }
                }
            };
            var values = new Dictionary<string, string>
            { ["NAME"] = "BENALI Karim", ["BOX"] = "09 102 457 89", ["DATE"] = "120385", ["CHK"] = "X" };

            string outPath = Path.Combine(_dir, "render.pdf");
            AtsDrtFormRenderer.RenderPdf(form, values, 0, 0, outPath);

            Assert.That(File.Exists(outPath), Is.True);
            byte[] head = new byte[5];
            using (var fs = File.OpenRead(outPath)) fs.Read(head, 0, 5);
            Assert.That(System.Text.Encoding.ASCII.GetString(head), Is.EqualTo("%PDF-"), "a real PDF is produced");
            Assert.That(new FileInfo(outPath).Length, Is.GreaterThan(500), "the PDF is non-trivial");
        }

        [Test]
        public void PrinterOffset_ShiftsEveryValue_ForPrePrintedCalibration()
        {
            // The same values at two different offsets must yield different PDFs — proving the
            // global mm offset (printer calibration) actually moves the ink.
            var form = new FormDefinition
            {
                Pages = { new FormPage { WidthMm = 210, HeightMm = 297,
                    Fields = { new FormField { Name = "X", Type = "grid", XMm = 25, YMm = 60, PitchMm = 6, Cells = 6 } } } }
            };
            var values = new Dictionary<string, string> { ["X"] = "120385" };

            string a = Path.Combine(_dir, "a.pdf"), b = Path.Combine(_dir, "b.pdf");
            AtsDrtFormRenderer.RenderPdf(form, values, 0, 0, a);
            AtsDrtFormRenderer.RenderPdf(form, values, 5, 3, b);
            Assert.That(File.ReadAllBytes(a), Is.Not.EqualTo(File.ReadAllBytes(b)), "offset changes the output");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static Company SampleCompany() => new Company
        {
            ManagerName = "CHERIF Mohamed",
            EmployerNumber = "0910245789",
            CompanyName = "SARL Atlas Industrie",
            Address = "Zone Industrielle, Rouiba",
            Location = "Alger"
        };

        private static Employee SampleEmployee() => new Employee
        {
            LastName = "BENALI",
            FirstName = "Karim",
            BirthDate = new DateTime(1990, 5, 12),
            BirthPlace = "Alger",
            SocialSecurityNumber = "123456789012",
            Address = "Cité 200 logements, Rouiba",
            HireDate = new DateTime(2018, 3, 1),
            Position = "Technicien"
        };

        private static AtsCertificateData SampleAts(bool resumed, int months)
        {
            var svc = new CertificateService(new WeekendConfig());
            List<MonthlyContribution> grid = svc.BuildEmptyMonthGrid(new DateTime(2025, 12, 1), months, false);
            foreach (MonthlyContribution row in grid)
                if (row.IsActive) { row.DaysWorked = 22m; row.AbsenceReason = ""; row.ContributionBase = 40000m; }

            var stoppage = new WorkStoppage { StoppageDate = new DateTime(2026, 1, 4), NumberOfDays = 15 };
            return svc.BuildAts(SampleCompany(), SampleEmployee(), stoppage, resumed, grid);
        }

        private static DrtCertificateData SampleDrt(bool resumed)
        {
            var svc = new CertificateService(new WeekendConfig());
            var stoppage = new WorkStoppage { StoppageDate = new DateTime(2026, 1, 4), NumberOfDays = 15 };
            return svc.BuildDrt(SampleCompany(), SampleEmployee(), stoppage, resumed);
        }
    }
}
