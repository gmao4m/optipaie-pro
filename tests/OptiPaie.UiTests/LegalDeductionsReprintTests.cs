using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using QuestPDF.Drawing;
using QuestPDF.Fluent;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// PROOF 4 — the trap. A payslip computed with the IRG/CNAS setting ON and saved must reprint
    /// to the SAME PDF bytes after the setting is switched OFF: a reprint reads the flag frozen on
    /// the payslip, never the current setting. Runs on a TEMP database (never the live one).
    /// PDF timestamps are pinned so the comparison is on the document's content, byte for byte.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public sealed class LegalDeductionsReprintTests
    {
        private static readonly DateTime Pinned = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [OneTimeSetUp]
        public void EnsureAppAndFonts()
        {
            // The payslip uses the app's embedded fonts, registered from pack URIs (needs the App).
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

        private static byte[] Pdf(FichePaieModel model) =>
            Document.Create(c => new FichePaieDocument(model).Compose(c))
                .WithMetadata(new DocumentMetadata { CreationDate = Pinned, ModifiedDate = Pinned })
                .GeneratePdf();

        [Test]
        public void Reprint_After_Switching_Off_Is_Byte_Identical()
        {
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-reprint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                AppServices services = CompositionRoot.Build(new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr"));
                long companyId = services.Companies.Create(new Company { NameFr = "SARL Reprint", Nif = "000000000000000" }).Value;
                long employeeId = services.Employees.Create(new Employee
                {
                    CompanyId = companyId, LastNameFr = "HADDAD", FirstNameFr = "Bilal", Poste = "Agent",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(2024, 1, 1), BaseSalary = 60000m, IsActive = true
                }).Value;

                DateTime last = DateTime.Today.AddMonths(-1);
                Assert.That(services.ConfigurationService.GetLegalDeductionsEnabled(), Is.True, "default ON");

                // 1) Compute with the setting ON and save.
                Result<long> saved = services.Payroll.Generate(new PayrollGenerationRequest
                {
                    CompanyId = companyId, EmployeeId = employeeId, Year = last.Year, Month = last.Month
                }, acknowledgeWarnings: true);
                Assert.That(saved.IsSuccess, Is.True, saved.Error);

                Company company = services.Companies.Get(companyId);
                Employee employee = services.Employees.Get(employeeId);
                var fiche = new FicheService();

                Payslip before = services.Archive.GetPayslip(saved.Value);
                Assert.That(before.LegalDeductionsApplied, Is.True);
                Assert.That(before.CnasEmployee, Is.GreaterThan(0m), "computed WITH deductions");
                byte[] pdfBefore = Pdf(fiche.FromPayslip(company, employee, before, false, last.Year, last.Month));

                // 2) Switch the setting OFF.
                Assert.That(services.ConfigurationService.SetLegalDeductionsEnabled(false).IsSuccess, Is.True);
                Assert.That(services.ConfigurationService.GetLegalDeductionsEnabled(), Is.False);

                // 3) Reprint the SAME saved payslip.
                Payslip after = services.Archive.GetPayslip(saved.Value);
                Assert.That(after.LegalDeductionsApplied, Is.True, "the saved payslip keeps its own flag");
                byte[] pdfAfter = Pdf(fiche.FromPayslip(company, employee, after, false, last.Year, last.Month));

                Assert.That(pdfAfter.Length, Is.GreaterThan(1000));
                Assert.That(pdfAfter, Is.EqualTo(pdfBefore), "reprint after switching OFF must be byte-identical");

                // Sensitivity: the comparison DOES see a difference when the flag differs, so it is not vacuous.
                FichePaieModel flipped = fiche.FromPayslip(company, employee, after, false, last.Year, last.Month);
                flipped.LegalDeductionsApplied = false;
                Assert.That(Pdf(flipped), Is.Not.EqualTo(pdfBefore), "the byte comparison must be able to fail");

                TestContext.Out.WriteLine("reprint PDF: " + pdfBefore.Length + " bytes before, " + pdfAfter.Length +
                                          " bytes after switching OFF — identical=" + pdfBefore.SequenceEqual(pdfAfter));
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }
    }
}
