using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Payroll;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// PART 3 — the read-only exposure diagnostic. Seeds a known catalog and asserts which
    /// elements are listed as exempt (gains outside a base, no cap) and which carry an
    /// unenforced ceiling. Expected membership is hand-reasoned from the seed (RULE 0).
    /// </summary>
    [TestFixture]
    public sealed class PayrollExposureServiceTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private PayrollExposureService _sut;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-exposure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
            _sut = new PayrollExposureService(_uow);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private void Add(string name, ElementType type, CalculationMethod method,
            bool cnas, bool irg, bool enabled, decimal? ceiling = null, decimal? fixedAmount = null)
        {
            using (IUnitOfWork uow = _uow.Create())
            {
                uow.PayrollElements.Insert(new PayrollElement
                {
                    NameFr = name, NameAr = name,
                    ElementType = type, CalculationMethod = method,
                    DefaultAmount = fixedAmount,
                    Periodicity = ElementPeriodicity.Monthly,
                    IsCnasApplicable = cnas, IsIrgApplicable = irg,
                    IsIncludedInGross = true, ExemptionCeiling = ceiling,
                    IsEditable = true, IsEnabled = enabled, IsSystem = false,
                    DisplayOrder = 1, IsPrintable = true, IncludedInLissage = false, IsAutomatic = false
                });
            }
        }

        // Unique "ZZ_" names so the test's own elements never collide with the built-in
        // elements the migration seeds (e.g. Prime de Panier/Transport).

        [Test]
        public void ListsExemptGains_WithScopeAndFixedAmount_ButNotSubjectOrDeductionOrDisabled()
        {
            Add("ZZ_Panier", ElementType.Gain, CalculationMethod.QuantityUnitPrice, cnas: false, irg: false, enabled: true);
            Add("ZZ_Transport", ElementType.Gain, CalculationMethod.FixedAmount, cnas: false, irg: false, enabled: true, fixedAmount: 3000m);
            Add("ZZ_Zone", ElementType.Gain, CalculationMethod.FixedAmount, cnas: true, irg: false, enabled: true);
            Add("ZZ_Subject", ElementType.Gain, CalculationMethod.Percentage, cnas: true, irg: true, enabled: true);   // subject -> excluded
            Add("ZZ_Deduction", ElementType.Deduction, CalculationMethod.FixedAmount, cnas: false, irg: false, enabled: true); // deduction -> excluded
            Add("ZZ_Disabled", ElementType.Gain, CalculationMethod.FixedAmount, cnas: false, irg: false, enabled: false); // disabled -> excluded

            PayrollExposureReport report = _sut.BuildCatalogExposure();
            string[] names = report.ExemptElements.Select(x => x.Name).ToArray();

            Assert.That(names, Does.Contain("ZZ_Panier"));
            Assert.That(names, Does.Contain("ZZ_Transport"));
            Assert.That(names, Does.Contain("ZZ_Zone"));
            Assert.That(names, Does.Not.Contain("ZZ_Subject"));
            Assert.That(names, Does.Not.Contain("ZZ_Deduction"));
            Assert.That(names, Does.Not.Contain("ZZ_Disabled"));

            ExemptElementInfo transport = report.ExemptElements.Single(x => x.Name == "ZZ_Transport");
            Assert.That(transport.ExemptFromContribution, Is.True);
            Assert.That(transport.ExemptFromTax, Is.True);
            Assert.That(transport.FixedMonthlyAmount, Is.EqualTo(3000m));

            ExemptElementInfo zone = report.ExemptElements.Single(x => x.Name == "ZZ_Zone");
            Assert.That(zone.ExemptFromContribution, Is.False, "zone is cotisable");
            Assert.That(zone.ExemptFromTax, Is.True, "zone is IRG-exempt");

            foreach (ExemptElementInfo info in report.ExemptElements)
            {
                Assert.That(info.NoteFr, Is.Not.Empty);
                Assert.That(info.NoteAr.Any(ch => ch >= '؀' && ch <= 'ۿ'), Is.True, "Arabic note for " + info.Name);
            }
        }

        [Test]
        public void ListsCeilingElements_AsUnenforced()
        {
            Add("ZZ_Plafonnee", ElementType.Gain, CalculationMethod.FixedAmount, cnas: false, irg: false, enabled: true, ceiling: 5000m);

            PayrollExposureReport report = _sut.BuildCatalogExposure();

            CeilingElementInfo c = report.CeilingElements.SingleOrDefault(x => x.Name == "ZZ_Plafonnee");
            Assert.That(c, Is.Not.Null, "the ceiling element is listed");
            Assert.That(c.Ceiling, Is.EqualTo(5000m));
            Assert.That(c.NoteFr.ToLowerInvariant(), Does.Contain("non appliqué"));
            Assert.That(c.NoteAr.Any(ch => ch >= '؀' && ch <= 'ۿ'), Is.True);
            Assert.That(report.HasExposure, Is.True);
        }
    }
}
