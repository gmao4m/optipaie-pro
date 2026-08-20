using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Auth;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The startup company gate DECISION (mandatory tests #5 and #6). The WPF windowing itself
    /// (picker modality, shell rebuild) is not headless-testable — same caveat as the login
    /// ShutdownMode flow — so the branching rule is proven here as a pure, WPF-free policy, and
    /// tied to the real company count via <see cref="CompanyService"/>.
    /// </summary>
    [TestFixture]
    public sealed class CompanyStartupGateTests
    {
        [Test]
        public void OneCompany_EntersDirectly_NoPicker()
        {
            // Test #5: a single company must open directly — no screen, no question, no click.
            Assert.That(CompanySelectionPolicy.Decide(1), Is.EqualTo(CompanyStartupAction.EnterDirect));
        }

        [Test]
        public void SeveralCompanies_ShowThePicker()
        {
            // Test #6: with more than one company the gate is a blocking picker — never a silent
            // auto-open of the first company (which would show one client's data by accident).
            Assert.That(CompanySelectionPolicy.Decide(2), Is.EqualTo(CompanyStartupAction.Choose));
            Assert.That(CompanySelectionPolicy.Decide(9), Is.EqualTo(CompanyStartupAction.Choose));
        }

        [Test]
        public void NoCompany_OffersCreation()
        {
            Assert.That(CompanySelectionPolicy.Decide(0), Is.EqualTo(CompanyStartupAction.CreateFirst));
        }

        [Test]
        public void NegativeCount_Throws()
        {
            Assert.That(() => CompanySelectionPolicy.Decide(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Decision_TracksTheRealCompanyCount()
        {
            string directory = Path.Combine(Path.GetTempPath(), "optipaie-gate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                SqliteTypeHandlers.Register();
                var factory = new SqliteConnectionFactory(Path.Combine(directory, "test.db"));
                using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

                IUnitOfWorkFactory uowf = new UnitOfWorkFactory(factory);
                var companies = new CompanyService(uowf, new CompanyValidator());

                // Empty database → create the first.
                Assert.That(CompanySelectionPolicy.Decide(companies.GetAll().Count),
                    Is.EqualTo(CompanyStartupAction.CreateFirst));

                // One company → direct entry.
                companies.Create(new Company { NameFr = "SARL Une", Nif = "000000000000000" });
                Assert.That(CompanySelectionPolicy.Decide(companies.GetAll().Count),
                    Is.EqualTo(CompanyStartupAction.EnterDirect));

                // A second company → the blocking picker.
                companies.Create(new Company { NameFr = "SARL Deux", Nif = "111111111111111" });
                Assert.That(CompanySelectionPolicy.Decide(companies.GetAll().Count),
                    Is.EqualTo(CompanyStartupAction.Choose));
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                try { Directory.Delete(directory, true); } catch (IOException) { }
            }
        }
    }
}
