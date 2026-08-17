using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Services.Documents;
using QuestPDF.Fluent;

namespace OptiPaie.Tests
{
    /// <summary>The three leave printouts (décision, attestation de solde, reliquat) generate a non-empty PDF without error.</summary>
    [TestFixture]
    public sealed class LeaveDocumentTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-leavedoc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { /* ignore */ }
        }

        [Test]
        public void Decision_GeneratesPdf()
        {
            var doc = new LeaveDecisionDocument(new LeaveDecisionModel
            {
                CompanyName = "SARL Test", EmployeeName = "BENALI Karim", TypeLabel = "Congé annuel",
                PaymentLabel = "Payé (employeur)", StartDate = new DateTime(2025, 6, 1), EndDate = new DateTime(2025, 6, 5),
                Days = 5m, DecisionDate = new DateTime(2025, 6, 1)
            });

            string path = Path.Combine(_dir, "decision.pdf");
            Assert.DoesNotThrow(() => Document.Create(doc.Compose).GeneratePdf(path));
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(500));
        }

        [Test]
        public void BalanceCertificate_GeneratesPdf()
        {
            var doc = new LeaveBalanceCertificateDocument(new LeaveBalanceCertificateModel
            {
                CompanyName = "SARL Test", EmployeeName = "BENALI Karim", Year = 2025,
                Entitlement = 30m, Taken = 12m, Pending = 3m, Available = 15m
            });

            string path = Path.Combine(_dir, "cert.pdf");
            Assert.DoesNotThrow(() => Document.Create(doc.Compose).GeneratePdf(path));
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(500));
        }

        [Test]
        public void Settlement_GeneratesPdf()
        {
            var doc = new LeaveSettlementDocument(new FinalSettlement
            {
                EmployeeName = "BENALI Karim", ExitDate = new DateTime(2025, 6, 30), Acquired = 15m, Taken = 0m,
                RemainingDays = 15m, MonthlySalary = 40000m, DailyRate = 40000m / 30m, Amount = 20000m
            }, "SARL Test");

            string path = Path.Combine(_dir, "settlement.pdf");
            Assert.DoesNotThrow(() => Document.Create(doc.Compose).GeneratePdf(path));
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(500));
        }
    }
}
