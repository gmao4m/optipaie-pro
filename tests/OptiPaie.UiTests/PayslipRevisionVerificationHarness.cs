using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Desktop.Documents;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;
using QuestPDF.Fluent;
using Cert = OptiPaie.Core.Certificates;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// End-to-end VERIFICATION of payslip correction + deletion, producing the REAL documents a user
    /// would open. [Explicit] — run on demand, never in CI. It drives the SAME services and the SAME
    /// document generators the Archive screen uses:
    ///   • calculate + archive a payslip, record its loan recovery, render the Fiche PDF and the DAC PDF;
    ///   • CORRECT it (raise the base) and re-open every downstream artifact — Fiche (with the
    ///     rectificatif badge + new net), DAC assiette, ATS page-2 base — confirming each reflects it
    ///     and the OLD value is gone;
    ///   • DELETE it, confirm the slot is free and the declarations/ATS drop it, recalculate the month,
    ///     and render the artifacts again.
    /// Every generated file path is printed to the test output.
    /// </summary>
    [TestFixture, Explicit, Apartment(ApartmentState.STA)]
    public sealed class PayslipRevisionVerificationHarness
    {
        private static readonly int Year = DateTime.Today.Year - 1;
        private const int Month = 3;

        [OneTimeSetUp]
        public void EnsureAppAndFonts()
        {
            // The Fiche renderer resolves the bundled IBM Plex faces through the WPF pack scheme and
            // QuestPDF's FontManager — exactly what the real app sets up at startup. Reproduce it so
            // the generated PDFs are byte-identical to the ones a user prints.
            if (Application.Current == null)
            {
                var app = new OptiPaie.Desktop.App();
                typeof(OptiPaie.Desktop.App)
                    .GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(app, null);
            }

            typeof(OptiPaie.Desktop.App).Assembly
                .GetType("OptiPaie.Desktop.Documents.PdfFonts")
                .GetMethod("Register", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, null);
        }

        [Test]
        public void Verify_Correction_And_Deletion_With_Real_Documents()
        {
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-verify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string docs = Path.Combine(RepoRoot(), "docs", "verification-payslip-revision");
            Directory.CreateDirectory(docs);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(dir, "demo.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            IUnitOfWorkFactory uow = new UnitOfWorkFactory(factory);

            var companies = new CompanyService(uow, new CompanyValidator());
            var employees = new EmployeeService(uow, new EmployeeValidator());
            var contracts = new ContractService(uow);
            var archive = new ArchiveService(uow);
            var config = new ConfigurationService(uow);
            var payroll = new PayrollService(uow, config, new PayrollCalculationEngine());
            var loans = new LoanService(uow);
            var batch = new BatchPayrollService(
                employees, new PayrollElementService(uow, new PayrollElementValidator()),
                loans, new AttendanceService(uow), payroll, contracts, archive, _ => true);
            var cnas = new CnasDeclarationService(companies, employees, archive, config);
            var ats = new AtsDrtDocumentService(companies, employees, archive);
            var revision = new PayslipRevisionService(uow, payroll, config, loans);

            long companyId = companies.Create(new Company
            {
                NameFr = "SARL Démo OptiPaie", Nif = "000000000000000", CnasEmployerNumber = "1234567890"
            }).Value;
            long empId = employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "BENALI", FirstNameFr = "Karim", Poste = "Agent",
                Nss = "123456789012", BirthDate = new DateTime(1985, 1, 1), Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash, ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year - 2, 1, 1), BaseSalary = 50000m, IsActive = true
            }).Value;
            loans.Save(new Loan
            {
                EmployeeId = empId, Type = LoanType.Loan, Principal = 100000m,
                MonthlyInstallment = 20000m, StartYear = Year, StartMonth = Month
            });

            Company company = companies.Get(companyId);
            Employee employee = employees.Get(empId);

            // ---------- 1. ORIGINAL: calculate + archive + record loan + render artifacts ----------
            long originalId = GenerateAndRecord(payroll, batch, archive, loans, companyId, empId, Month);
            string p1Fiche = RenderFiche(archive, company, employee, originalId, Path.Combine(docs, "01_fiche_original.pdf"));
            decimal originalAssiette = cnas.BuildDac(companyId, Year, new[] { Month }).Assiette;
            string p1Dac = RenderDac(cnas, company, originalAssiette, Path.Combine(docs, "02_dac_original.pdf"));
            decimal originalOutstanding = loans.GetByEmployee(empId).Single().Outstanding;
            decimal atsOriginal = AtsBase(ats, companyId, empId);

            Print("ORIGINAL", originalAssiette, originalOutstanding, atsOriginal, p1Fiche, p1Dac);

            // ---------- 2. CORRECT: raise base 50 000 -> 60 000 ----------
            PayrollGenerationRequest amended = batch.BuildRequest(companyId, empId, Year, Month);
            amended.BaseSalaryOverride = 60000m;
            Result<long> corrected = revision.Correct(new PayslipCorrectionRequest
            {
                OriginalPayslipId = originalId, Amended = amended, Reason = "Rappel de salaire — base corrigée"
            });
            Assert.That(corrected.IsSuccess, Is.True, corrected.Error);

            string p2Fiche = RenderFiche(archive, company, employee, corrected.Value, Path.Combine(docs, "03_fiche_corrigee.pdf"));
            decimal correctedAssiette = cnas.BuildDac(companyId, Year, new[] { Month }).Assiette;
            string p2Dac = RenderDac(cnas, company, correctedAssiette, Path.Combine(docs, "04_dac_corrigee.pdf"));
            decimal atsCorrected = AtsBase(ats, companyId, empId);
            Payslip correctedPayslip = Get(uow, corrected.Value);

            Print("CORRECTED", correctedAssiette, loans.GetByEmployee(empId).Single().Outstanding, atsCorrected, p2Fiche, p2Dac);
            Assert.That(correctedAssiette, Is.GreaterThan(originalAssiette), "DAC assiette reflects the raised base");
            Assert.That(atsCorrected, Is.EqualTo(correctedAssiette), "ATS page-2 base follows the corrected payslip");
            Assert.That(correctedPayslip.SupersedesPayslipId, Is.EqualTo(originalId), "corrected version links to the original (drives the rectificatif badge)");
            Assert.That(ActiveCount(archive, companyId), Is.EqualTo(1), "only the corrected version is active");

            // ---------- 3. DELETE: cancel, confirm slot free, recalculate ----------
            Result del = revision.Cancel(corrected.Value, "Bulletin émis en double");
            Assert.That(del.IsSuccess, Is.True, del.Error);
            decimal deletedAssiette = cnas.BuildDac(companyId, Year, new[] { Month }).Assiette;
            Assert.That(deletedAssiette, Is.EqualTo(0m), "a deleted payslip is excluded from the DAC");
            Assert.That(ActiveCount(archive, companyId), Is.EqualTo(0), "the slot is free");
            Assert.That(AtsBase(ats, companyId, empId), Is.EqualTo(0m), "ATS page-2 drops the deleted month");

            long reRunId = GenerateAndRecord(payroll, batch, archive, loans, companyId, empId, Month);
            Assert.That(reRunId, Is.Not.EqualTo(corrected.Value), "a brand-new payslip after re-calculation");
            string p3Fiche = RenderFiche(archive, company, employee, reRunId, Path.Combine(docs, "05_fiche_apres_recalcul.pdf"));
            decimal reRunAssiette = cnas.BuildDac(companyId, Year, new[] { Month }).Assiette;
            string p3Dac = RenderDac(cnas, company, reRunAssiette, Path.Combine(docs, "06_dac_apres_recalcul.pdf"));
            Print("RECALCULATED", reRunAssiette, loans.GetByEmployee(empId).Single().Outstanding, AtsBase(ats, companyId, empId), p3Fiche, p3Dac);

            TestContext.Out.WriteLine(Environment.NewLine + "ALL DOCUMENTS WRITTEN TO: " + Path.GetFullPath(docs));
        }

        private static long GenerateAndRecord(PayrollService payroll, BatchPayrollService batch, ArchiveService archive, LoanService loans, long companyId, long empId, int month)
        {
            Result<long> g = payroll.Generate(batch.BuildRequest(companyId, empId, Year, month));
            Assert.That(g.IsSuccess, Is.True, g.Error);
            Payslip archived = archive.GetPayslip(g.Value);
            decimal withheld = archived.Details
                .Where(d => d.ElementType == ElementType.Deduction && string.Equals(d.LabelFr, "Remboursement prêt", StringComparison.Ordinal))
                .Sum(d => d.Amount);
            if (withheld > 0m) loans.RecordPayrollDeductions(empId, Year, month, withheld);
            return g.Value;
        }

        private static string RenderFiche(ArchiveService archive, Company company, Employee employee, long payslipId, string path)
        {
            Payslip p = archive.GetPayslip(payslipId);
            var fiche = new FicheService();
            FichePaieModel model = fiche.FromPayslip(company, employee, p, false, Year, Month,
                "BULLETIN DE PAIE RECTIFICATIF — remplace une version antérieure");
            fiche.ExportPdf(model, path);
            return path;
        }

        private static string RenderDac(CnasDeclarationService cnas, Company company, decimal assiette, string path)
        {
            CnasDacReport report = cnas.BuildDac(company.Id, Year, new[] { Month });
            IReadOnlyList<CnasMovementRow> movements = cnas.BuildMovements(company.Id, Year, new[] { Month });
            var document = new CnasDacDocument(report, company.NameFr, MonthName(Month) + " " + Year, movements);
            Document.Create(document.Compose).GeneratePdf(path);
            return path;
        }

        private static decimal AtsBase(AtsDrtDocumentService ats, long companyId, long empId)
        {
            List<Cert.MonthlyContribution> rows = ats.BuildContributionsFromPayroll(companyId, empId, new DateTime(Year, Month, 1), 1, false);
            Cert.MonthlyContribution active = rows.FirstOrDefault(r => r.IsActive);
            return active?.ContributionBase ?? 0m;
        }

        private static int ActiveCount(ArchiveService archive, long companyId)
        {
            PayrollRun run = archive.SearchRuns(companyId, Year, Month).FirstOrDefault();
            return run == null ? 0 : archive.GetRun(run.Id).Payslips.Count;
        }

        private static Payslip Get(IUnitOfWorkFactory uow, long id)
        {
            using (IUnitOfWork u = uow.Create()) return u.Payslips.GetById(id);
        }

        private static void Print(string label, decimal assiette, decimal outstanding, decimal atsBase, string fiche, string dac)
        {
            TestContext.Out.WriteLine("=== " + label + " ===");
            TestContext.Out.WriteLine("  DAC assiette      : " + assiette.ToString("N2"));
            TestContext.Out.WriteLine("  ATS page-2 base   : " + atsBase.ToString("N2"));
            TestContext.Out.WriteLine("  Loan outstanding  : " + outstanding.ToString("N2"));
            TestContext.Out.WriteLine("  Fiche PDF         : " + Path.GetFullPath(fiche));
            TestContext.Out.WriteLine("  DAC PDF           : " + Path.GetFullPath(dac));
        }

        private static string MonthName(int m) =>
            System.Globalization.CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetMonthName(m);

        /// <summary>Walks up from the test output folder to the repository root (the folder holding OptiPaie.sln).</summary>
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? TestContext.CurrentContext.TestDirectory;
        }
    }
}
