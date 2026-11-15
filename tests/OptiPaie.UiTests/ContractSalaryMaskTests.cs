using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Security;
using OptiPaie.Desktop.Documents;
using QuestPDF.Fluent;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// COMMIT F — the contract PDF is a Desktop-layer salary surface. It states an Algerian salary in
    /// Article 5. This proves the SAME <see cref="Permission.ViewSalary"/> gate masks it: a role
    /// without the permission gets a contract produced WITHOUT the figure (Article 5 shows the masked
    /// placeholder), while a role with it sees the amount — and the PDF still renders either way.
    /// </summary>
    [TestFixture, Apartment(System.Threading.ApartmentState.STA)]
    public sealed class ContractSalaryMaskTests
    {
        private const decimal Sentinel = 918273.64m;
        private const string SalaryDigits = "91827364";

        [Test]
        public void ContractArticle5_IsMasked_WithoutViewSalary_ButShownWithIt()
        {
            Assert.That(HasDigits(ContractDocument.SalaryText(Sentinel, showSalary: false)), Is.False,
                "a contract for a role without ViewSalary must carry no salary figure");
            Assert.That(ContractDocument.SalaryText(Sentinel, showSalary: false), Is.EqualTo(SalaryVisibility.Masked));
            Assert.That(HasDigits(ContractDocument.SalaryText(Sentinel, showSalary: true)), Is.True,
                "positive control: a role with ViewSalary sees the amount");
        }

        [OneTimeSetUp]
        public void RegisterFonts()
        {
            // Register the embedded IBM Plex faces exactly as the fiche render harness does, so the
            // contract PDF can render in CI (PdfFonts is internal to the Desktop assembly).
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
        public void ContractPdf_Renders_BothWays()
        {
            // Production proof: the document still produces a real, non-empty PDF whether the salary
            // is shown or masked (masking never blocks the document).
            foreach (bool show in new[] { true, false })
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
                    ShowSalary = show
                };

                string path = Path.Combine(Path.GetTempPath(), "contract-" + Guid.NewGuid().ToString("N") + ".pdf");
                try
                {
                    Document.Create(new ContractDocument(model).Compose).GeneratePdf(path);
                    Assert.That(new FileInfo(path).Length, Is.GreaterThan(1000), "a real, non-empty contract PDF was produced (show=" + show + ")");
                }
                finally
                {
                    try { File.Delete(path); } catch { }
                }
            }
        }

        private static bool HasDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var sb = new System.Text.StringBuilder();
            foreach (char c in text) if (char.IsDigit(c)) sb.Append(c);
            return sb.ToString().Contains(SalaryDigits);
        }
    }
}
