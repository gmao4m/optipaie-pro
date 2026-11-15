using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Documents;
using QuestPDF.Fluent;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// COMMIT H — the salary masking is proven on the BYTES the customer receives. Each PDF is
    /// rendered to a real file, its text is extracted with PdfPig (which reads QuestPDF's compressed,
    /// glyph-encoded streams), and the distinctive salary figure is asserted ABSENT when the document
    /// is produced by a role without ViewSalary and PRESENT when produced by a role with it (positive
    /// control). No string-model shortcut.
    /// </summary>
    [TestFixture, Apartment(System.Threading.ApartmentState.STA)]
    public sealed class SalaryPdfByteTests
    {
        private const decimal Sentinel = 918273.64m;
        private const string SalaryDigits = "91827364";

        [OneTimeSetUp]
        public void Boot()
        {
            if (System.Windows.Application.Current == null)
            {
                var app = new OptiPaie.Desktop.App();
                typeof(OptiPaie.Desktop.App)
                    .GetMethod("InitializeComponent", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(app, null);
            }
            typeof(OptiPaie.Desktop.App).Assembly
                .GetType("OptiPaie.Desktop.Documents.PdfFonts")
                .GetMethod("Register", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Invoke(null, null);
        }

        [Test]
        public void PdfPig_CanRead_QuestPdfText_SanityCheck()
        {
            // Proves the extractor actually reads QuestPDF output — otherwise every "absent" below
            // would be meaningless.
            byte[] pdf = ContractPdf(showSalary: true);
            Assert.That(PdfText(pdf), Does.Contain("Rémunération"), "PdfPig must read QuestPDF text");
        }

        [Test]
        public void ContractPdf_Bytes_MaskedWithoutViewSalary()
        {
            Assert.That(HasSalary(PdfText(ContractPdf(showSalary: false))), Is.False,
                "contract PDF (bytes): no salary without ViewSalary");
            Assert.That(HasSalary(PdfText(ContractPdf(showSalary: true))), Is.True,
                "positive control: the amount IS in the bytes with ViewSalary");
        }

        private static byte[] ContractPdf(bool showSalary)
        {
            var model = new ContractDocumentModel
            {
                Company = new Company { NameFr = "SARL Test" },
                Employee = new Employee { LastNameFr = "Benali", FirstNameFr = "Karim" },
                Contract = new EmploymentContract
                {
                    Type = ContractType.Cdi, Status = ContractStatus.Active,
                    StartDate = new DateTime(2020, 1, 1), BaseSalary = Sentinel, Position = "Comptable"
                },
                ShowSalary = showSalary
            };
            return Render(new ContractDocument(model).Compose);
        }

        [Test]
        public void WorkCertificatePdf_Bytes_MaskedWithoutViewSalary()
        {
            Assert.That(HasSalary(PdfText(CertificatePdf(showSalary: false))), Is.False,
                "salary certificate PDF (bytes): no salary without ViewSalary");
            Assert.That(HasSalary(PdfText(CertificatePdf(showSalary: true))), Is.True,
                "positive control: the amount IS in the bytes with ViewSalary");
        }

        private static byte[] CertificatePdf(bool showSalary)
        {
            var model = new OptiPaie.Core.Dtos.CertificateRenderModel
            {
                Certificate = new WorkCertificate
                {
                    Type = CertificateType.SalaryCertificate,
                    IssueDate = new DateTime(2024, 6, 1), Reference = "C-1"
                },
                Employee = new Employee { LastNameFr = "Benali", FirstNameFr = "Karim", HireDate = new DateTime(2020, 1, 1) },
                Company = new Company { NameFr = "SARL Test" },
                MonthlySalary = Sentinel,
                ShowSalary = showSalary
            };
            return Render(new OptiPaie.Services.Documents.WorkCertificateDocument(model).Compose);
        }

        [Test]
        public void LeaveSettlementPdf_Bytes_MaskedWithoutViewSalary()
        {
            Assert.That(HasSalary(PdfText(SettlementPdf(showSalary: false))), Is.False,
                "leave settlement PDF (bytes): no salary without ViewSalary");
            Assert.That(HasSalary(PdfText(SettlementPdf(showSalary: true))), Is.True,
                "positive control: the amount IS in the bytes with ViewSalary");
        }

        private static byte[] SettlementPdf(bool showSalary)
        {
            var settlement = new OptiPaie.Core.Dtos.FinalSettlement
            {
                EmployeeId = 1, EmployeeName = "Benali Karim", ExitDate = new DateTime(2024, 6, 30),
                Acquired = 10m, Taken = 2m, RemainingDays = 8m,
                MonthlySalary = Sentinel, DailyRate = Sentinel, Amount = Sentinel,
                ShowSalary = showSalary
            };
            return Render(new OptiPaie.Services.Documents.LeaveSettlementDocument(settlement, "SARL Test").Compose);
        }

        [Test]
        public void PayslipPdf_IsRefused_WithoutViewSalary_AndCarriesTheSalaryWithIt()
        {
            // The payslip is ALL salary — there is no masked payslip. A role without ViewSalary (and
            // without payroll export) is REFUSED outright, so no payslip bytes ever reach them.
            var fiche = new FicheService { Access = Ctx(UserRole.ReadOnly) };
            Assert.Throws<OptiPaie.Core.Security.AccessDeniedException>(
                () => fiche.ExportPdf(Payslip(), Path.Combine(Path.GetTempPath(), "x-" + Guid.NewGuid().ToString("N") + ".pdf")),
                "a role without ViewSalary cannot produce a payslip at all");

            // Positive control: a payroll role produces a payslip whose BYTES carry the salary.
            string path = Path.Combine(Path.GetTempPath(), "slip-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                new FicheService { Access = Ctx(UserRole.Payroll) }.ExportPdf(Payslip(), path);
                Assert.That(HasSalary(PdfText(File.ReadAllBytes(path))), Is.True, "the payroll payslip bytes carry the salary");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        private static FichePaieModel Payslip()
        {
            return new FichePaieModel
            {
                Company = new Company { NameFr = "SARL Test" },
                Employee = new Employee { LastNameFr = "Benali", FirstNameFr = "Karim" },
                Year = 2024, Month = 6,
                SalaireBrut = Sentinel, BaseCotisable = Sentinel, NetSalaire = Sentinel
            };
        }

        private static OptiPaie.Core.Security.IAccessContext Ctx(UserRole role) =>
            new Access { Role = role };

        // ---- helpers ----

        internal static byte[] Render(Action<QuestPDF.Infrastructure.IDocumentContainer> compose)
        {
            string path = Path.Combine(Path.GetTempPath(), "pdf-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                Document.Create(compose).GeneratePdf(path);
                return File.ReadAllBytes(path);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        internal static string PdfText(byte[] pdf)
        {
            var sb = new StringBuilder();
            using (var doc = UglyToad.PdfPig.PdfDocument.Open(pdf))
                foreach (UglyToad.PdfPig.Content.Page page in doc.GetPages())
                    sb.Append(page.Text).Append(' ');
            return sb.ToString();
        }

        internal static bool HasSalary(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text) if (char.IsDigit(c)) sb.Append(c);
            return sb.ToString().Contains(SalaryDigits);
        }

        private sealed class Access : OptiPaie.Core.Security.IAccessContext
        {
            public UserRole Role { get; set; }
            public bool IsEnforced => true;
            public UserRole CurrentRole => Role;
            public bool IsOwner => false;
            public long? CurrentUserId => null;
        }
    }
}
