using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Certificates;
using OptiPaie.Services.Validation;
using Cert = OptiPaie.Core.Certificates;

namespace OptiPaie.Tests
{
    /// <summary>
    /// MANDATORY end-to-end verification for the two ATS printing defects. Seeds a real company +
    /// employee + several months of persisted payroll, then drives the EXACT production path the
    /// desktop uses (MapCompany / MapEmployee / BuildContributionsFromPayroll / GenerateAts) to emit
    /// the real PDF a client prints, plus 300-dpi PNGs of both pages rendered from the identical
    /// values dictionary — so every field can be inspected against the official AS.08 layout.
    ///
    /// Explicit: it writes files to the session scratchpad; run it deliberately, not in CI.
    /// </summary>
    [TestFixture, Explicit]
    public sealed class AtsEndToEndVerification
    {
        private static readonly string Repo = @"C:\Users\PC\Desktop\OptiPaie PRO";
        private static readonly string RepoAssets =
            Path.Combine(Repo, "src", "OptiPaie.Desktop", "Assets", "AtsDrtTemplates");
        // Machine-independent output folder — the produced PDF + page PNGs land here for inspection.
        private static readonly string OutDir =
            Path.Combine(Path.GetTempPath(), "optipaie-ats-verify");

        private static readonly int Year = DateTime.Today.Year - 1;

        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private ContractService _contracts;
        private ArchiveService _archive;
        private ConfigurationService _config;
        private PayrollService _payroll;
        private BatchPayrollService _batch;
        private CnasDeclarationService _cnas;
        private AtsDrtDocumentService _ats;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-ats-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _contracts = new ContractService(_uow);
            _archive = new ArchiveService(_uow);
            _config = new ConfigurationService(_uow);
            _payroll = new PayrollService(_uow, _config, new PayrollCalculationEngine());
            _batch = new BatchPayrollService(
                _employees, new PayrollElementService(_uow, new PayrollElementValidator()),
                new LoanService(_uow), new AttendanceService(_uow), _payroll, _contracts, _archive, _ => true);
            _cnas = new CnasDeclarationService(_companies, _employees, _archive, _config);
            _ats = new AtsDrtDocumentService(_companies, _employees, _archive);

            // Make GenerateAts's FormsDir() (AppContext.BaseDirectory\Assets\AtsDrtTemplates) resolve
            // to the SHIPPED coordinate file + official form scans — the exact assets a client has.
            string binAssets = Path.Combine(AppContext.BaseDirectory, "Assets", "AtsDrtTemplates");
            Directory.CreateDirectory(binAssets);
            foreach (string f in new[] { "form-layout.json", "ATS_image1.jpeg", "ATS_image2.jpeg", "DRTFR_image1.png" })
                File.Copy(Path.Combine(RepoAssets, f), Path.Combine(binAssets, f), overwrite: true);

            Directory.CreateDirectory(OutDir);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void Generate_Ordinary_Attestation_With_Real_Payroll()
        {
            // ── 1. A demo employer + employee, hired well before the window. ──────────────
            long companyId = _companies.Create(new Company
            {
                NameFr = "SARL Atlas Industrie", Nif = "000000000000000", CnasEmployerNumber = "0910245789",
                AddressFr = "Zone Industrielle, Lot 24, Boufarik, Blida", ManagerName = "CHERIF Mohamed", City = "Blida"
            }).Value;

            long empId = _employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "BENALI", FirstNameFr = "Karim",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(Year - 2, 3, 1), BaseSalary = 50000m, IsActive = true,
                Nss = "123456789012", BirthDate = new DateTime(1985, 3, 12), BirthPlace = "Blida",
                Address = "Cité 200 logements, Bt C, Blida", Poste = "Comptable"
            }).Value;

            // ── 2. Six months of REAL persisted payroll (Jan..Jun of the reference year). ──
            var months = new[] { 1, 2, 3, 4, 5, 6 };
            foreach (int m in months) _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, m));

            // ── 3. Drive the exact production mapping the desktop uses. ────────────────────
            Cert.Company company = _ats.MapCompany(companyId);
            Cert.Employee employee = _ats.MapEmployee(empId);
            List<Cert.MonthlyContribution> contributions =
                _ats.BuildContributionsFromPayroll(companyId, empId, new DateTime(Year, 1, 1), 6, arabicMonthNames: false);

            // The ordinary employment+salary attestation: NOT a work-stoppage case.
            var stoppage = new Cert.WorkStoppage { StoppageDate = DateTime.Today, NumberOfDays = 0 };
            string pdfPath = Path.Combine(OutDir, "VERIFY_ATS_ordinary.pdf");
            _ats.GenerateAts(company, employee, stoppage, hasResumedWork: false, contributions,
                weekend: new Cert.WeekendConfig(), offsetXmm: 0, offsetYmm: 0, outputPdfPath: pdfPath,
                isWorkStoppage: false);

            // ── 4. Rebuild the SAME values dict GenerateAts used, for field-by-field review. ─
            Cert.AtsCertificateData data = new Cert.CertificateService(new Cert.WeekendConfig())
                .BuildAts(company, employee, stoppage, false, contributions, isWorkStoppage: false);
            Dictionary<string, string> v = Cert.CertificateBookmarkMapper.MapAts(data);

            // Render both pages at 300 dpi (official scan + black values) for visual inspection.
            Cert.FormLayoutConfig cfg = AtsDrtFormRenderer.LoadLayout(Path.Combine(RepoAssets, "form-layout.json"));
            Cert.FormDefinition ats = cfg.Forms["ATS"];
            string bg1 = Path.Combine(RepoAssets, ats.Pages[0].BackgroundImage);
            string bg2 = Path.Combine(RepoAssets, ats.Pages[1].BackgroundImage);
            AtsDrtFormRenderer.RenderPrintPreviewPng(ats.Pages[0], v, bg1, 0, 0, 300, Path.Combine(OutDir, "VERIFY_ATS_page1.png"));
            AtsDrtFormRenderer.RenderPrintPreviewPng(ats.Pages[1], v, bg2, 0, 0, 300, Path.Combine(OutDir, "VERIFY_ATS_page2.png"));

            // ── 5. Print a textual field dump so the mapping is auditable in the log. ──────
            TestContext.Out.WriteLine("=== PAGE 1 — identity + work-stoppage dates ===");
            foreach (string k in new[] { "NMEMPLOYEUR", "NEMPLOYEUR", "RS", "EADRESSE", "NMS", "NSS", "NEA",
                                          "POSTE", "SADRESSE", "DATEN", "DATENAR", "NEAAR", "DATER",
                                          "DATEAT", "DATEREPRISE", "DATEAUJRH", "LIEU", "AUJOURDHUI" })
                TestContext.Out.WriteLine($"  {k,-12} = [{v[k]}]");

            TestContext.Out.WriteLine("=== PAGE 2 — 12-month reference table (M / JT / MT / SS / PO) ===");
            for (int n = 1; n <= 12; n++)
                TestContext.Out.WriteLine(
                    $"  row {n,2}: M=[{v["M" + n]}]  JT=[{v["JT" + n]}]  MT=[{v["MT" + n]}]  SS=[{v["SS" + n]}]  PO=[{v["PO" + n]}]");

            // ── 6. DEFECT 1 — the three work-stoppage date fields must be blank on an ordinary ATS.
            Assert.That(v["DATEAT"], Is.Empty, "Date du dernier jour de travail must be blank (work-stoppage only)");
            Assert.That(v["DATEREPRISE"], Is.Empty, "Date de reprise de travail must be blank (work-stoppage only)");
            Assert.That(v["DATEAUJRH"], Is.Empty, "'n'a pas repris à ce jour' date must be blank (work-stoppage only)");
            // …while the general employment facts ARE printed.
            Assert.That(v["DATER"], Is.Not.Empty, "Date de recrutement is a general fact, always printed");
            Assert.That(v["NMS"], Is.EqualTo("BENALI Karim"));

            // ── 7. DEFECT 2 — every labelled month carries real data, never the "/" failure glyph.
            for (int n = 1; n <= 6; n++)
            {
                Assert.That(v["M" + n], Is.Not.Empty, $"month {n} label present");
                Assert.That(v["SS" + n], Is.Not.EqualTo("/"), $"SS{n}: salaire soumis à cotisations is real data");
                Assert.That(v["SS" + n], Does.Contain("DA"), $"SS{n} shows an amount");
                Assert.That(v["PO" + n], Is.Not.EqualTo("/"), $"PO{n}: part ouvrière is real data");
                Assert.That(v["JT" + n], Is.Not.EqualTo("/"), $"JT{n}: jours travaillés is real data");
            }
            // Unused rows 7..12 remain the official "/" (néant) — distinct from a failure.
            for (int n = 7; n <= 12; n++)
                Assert.That(v["SS" + n], Is.EqualTo("/"), $"SS{n}: unused row is struck '/'");

            // ── 8. The page-2 base equals the persisted Payslip.BaseCotisable AND the CNAS DAC assiette.
            List<Cert.MonthlyContribution> active = contributions.Where(r => r.IsActive).ToList();
            Assert.That(active.Count, Is.EqualTo(6));
            for (int i = 0; i < months.Length; i++)
            {
                PayrollRun run = _archive.GetRun(_archive.SearchRuns(companyId, Year, months[i]).First().Id);
                decimal persisted = run.Payslips.Single(p => p.EmployeeId == empId).BaseCotisable;
                decimal dac = _cnas.BuildDac(companyId, Year, new[] { months[i] }).Assiette;
                Assert.That(active[i].ContributionBase, Is.EqualTo(persisted), "ATS base == persisted BaseCotisable");
                Assert.That(active[i].ContributionBase, Is.EqualTo(dac), "ATS base == CNAS DAC assiette");
                Assert.That(active[i].EmployeeShare, Is.EqualTo(Math.Round(persisted * 0.09m, 2)), "part ouvrière = 9%");
            }

            Assert.That(File.Exists(pdfPath), Is.True);
            TestContext.Out.WriteLine("PDF : " + pdfPath);
            TestContext.Out.WriteLine("PNG1: " + Path.Combine(OutDir, "VERIFY_ATS_page1.png"));
            TestContext.Out.WriteLine("PNG2: " + Path.Combine(OutDir, "VERIFY_ATS_page2.png"));
        }
    }
}
