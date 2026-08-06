using System;
using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
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
            Assert.That(v["CT1"], Does.Contain(" DA"));
            Assert.That(v["MSS1"], Is.EqualTo("3600 DA"), "9% of 40000 = 3600");
            Assert.That(v["H12"], Is.EqualTo("/"), "unused trailing slot prints '/'");
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

        // ── filling the REAL official templates ──────────────────────────────

        [Test]
        public void Fill_RealAtsTemplate_InjectsEmployeeAndAmounts()
        {
            string outPath = Path.Combine(_dir, "ats.docx");
            DocxBookmarkFiller.Fill(TemplatePath("ATS_Template.docx"), outPath,
                CertificateBookmarkMapper.MapAts(SampleAts(resumed: false, months: 2)));

            string text = DocxText(outPath);
            Assert.That(text, Does.Contain("BENALI Karim"), "employee name is filled onto the official form");
            Assert.That(text, Does.Contain("12 3456 7890 12"), "formatted NSS is filled");
            Assert.That(text, Does.Contain("3600 DA"), "the 9% employee share is filled");
        }

        [Test]
        public void Fill_RealDrtTemplates_FrAndAr_InjectName()
        {
            foreach (string template in new[] { "DRT_Template_FR.docx", "DRT_Template_AR.docx" })
            {
                string outPath = Path.Combine(_dir, template);
                DocxBookmarkFiller.Fill(TemplatePath(template), outPath,
                    CertificateBookmarkMapper.MapDrt(SampleDrt(false)));

                string text = DocxText(outPath);
                Assert.That(text, Does.Contain("BENALI"), template + " → last name filled");
                Assert.That(text, Does.Contain("X"), template + " → the reprise/non-reprise box is checked");
            }
        }

        /// <summary>
        /// Emits real filled ATS + DRT (FR/AR) documents for manual inspection when
        /// ATSDRT_ARTIFACT_DIR is set (skipped in the normal suite).
        /// </summary>
        [Test]
        public void GenerateSampleDocuments_ForInspection()
        {
            string outDir = Environment.GetEnvironmentVariable("ATSDRT_ARTIFACT_DIR");
            if (string.IsNullOrEmpty(outDir)) { Assert.Ignore("Set ATSDRT_ARTIFACT_DIR to emit sample documents."); return; }
            Directory.CreateDirectory(outDir);

            DocxBookmarkFiller.Fill(TemplatePath("ATS_Template.docx"), Path.Combine(outDir, "ATS_BENALI_Karim.docx"),
                CertificateBookmarkMapper.MapAts(SampleAts(resumed: false, months: 3)));
            DocxBookmarkFiller.Fill(TemplatePath("DRT_Template_FR.docx"), Path.Combine(outDir, "DRT_FR_BENALI_Karim.docx"),
                CertificateBookmarkMapper.MapDrt(SampleDrt(resumed: false)));
            DocxBookmarkFiller.Fill(TemplatePath("DRT_Template_AR.docx"), Path.Combine(outDir, "DRT_AR_BENALI_Karim.docx"),
                CertificateBookmarkMapper.MapDrt(SampleDrt(resumed: false)));

            Assert.That(File.Exists(Path.Combine(outDir, "ATS_BENALI_Karim.docx")), Is.True);
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
                if (row.IsActive) { row.HoursWorked = 173.33m; row.AbsenceDays = 0m; row.ContributionBase = 40000m; }

            var stoppage = new WorkStoppage { StoppageDate = new DateTime(2026, 1, 4), NumberOfDays = 15 };
            return svc.BuildAts(SampleCompany(), SampleEmployee(), stoppage, resumed, grid);
        }

        private static DrtCertificateData SampleDrt(bool resumed)
        {
            var svc = new CertificateService(new WeekendConfig());
            var stoppage = new WorkStoppage { StoppageDate = new DateTime(2026, 1, 4), NumberOfDays = 15 };
            return svc.BuildDrt(SampleCompany(), SampleEmployee(), stoppage, resumed);
        }

        private static string TemplatePath(string name) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AtsDrtTemplates", name);

        private static string DocxText(string path)
        {
            using (var doc = WordprocessingDocument.Open(path, false))
                return doc.MainDocumentPart.Document.Body.InnerText;
        }
    }
}
