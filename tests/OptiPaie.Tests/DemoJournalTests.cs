using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The demo activity journal. Reproduces the EXACT production path — the same
    /// <see cref="DemoDataSeeder"/>, the same audit choke point (<see cref="AuditService"/> with a
    /// <c>CompanyProvider</c> tied to the active company, exactly as CompositionRoot wires it) — and
    /// proves the sales demo opens with a POPULATED journal, not an empty one.
    /// </summary>
    [TestFixture]
    public sealed class DemoJournalTests
    {
        private string _directory;
        private UnitOfWorkFactory _uowf;
        private long _activeCompany;      // mirrors CompanyContext.ActiveId
        private AuditService _audit;

        private CompanyService _companies;
        private EmployeeService _employees;
        private ContractService _contracts;
        private AttendanceService _attendance;
        private LeaveService _leave;
        private LoanService _loans;
        private AssetService _assets;
        private TrainingService _training;
        private WorkCertificateService _certificates;
        private PerformanceService _performance;

        [SetUp]
        public void SetUp()
        {
            _activeCompany = 0; // reset per test (NUnit shares one fixture instance)
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-demojournal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uowf = new UnitOfWorkFactory(factory);

            // The audit trail wired exactly like CompositionRoot: the company is resolved at record
            // time from the (mutable) active company, so whatever the seeder activates is attributed.
            _audit = new AuditService(_uowf, NullLogger.Instance, "Démo")
            {
                CompanyProvider = () => _activeCompany > 0 ? _activeCompany : (long?)null
            };

            _companies = new CompanyService(_uowf, new CompanyValidator());
            _employees = new EmployeeService(_uowf, new EmployeeValidator()) { Audit = _audit };
            _contracts = new ContractService(_uowf) { Audit = _audit };
            _attendance = new AttendanceService(_uowf);
            _leave = new LeaveService(_uowf) { Audit = _audit };
            _loans = new LoanService(_uowf) { Audit = _audit };
            _assets = new AssetService(_uowf) { Audit = _audit };
            _training = new TrainingService(_uowf);
            _certificates = new WorkCertificateService(_uowf);
            _performance = new PerformanceService(_uowf);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        private DemoDataSeeder NewSeeder()
        {
            return new DemoDataSeeder(_companies, _employees, _contracts, _attendance, _leave,
                _loans, _assets, _training, _certificates, _performance);
        }

        [Test]
        public void Demo_ActivityJournal_IsPopulated_NotEmpty()
        {
            var seeder = NewSeeder();
            // The production hook: activate the demo company the moment it is created.
            seeder.CompanyActivated = id => _activeCompany = id;

            Result<long> result = seeder.Seed();
            Assert.That(result.IsSuccess, Is.True, result.Error);
            long companyId = result.Value;

            // The dashboard feed: exactly what DashboardViewModel shows (company-scoped, top 12).
            IReadOnlyList<AuditEntry> feed = _audit.GetRecentForCompany(companyId, 12);
            IReadOnlyList<AuditEntry> everything = _audit.GetRecentForCompany(companyId, 100000);

            TestContext.WriteLine("DEMO JOURNAL lignes affichées (top 12) = " + feed.Count);
            TestContext.WriteLine("DEMO JOURNAL total attribué à la société = " + everything.Count);

            Assert.That(feed.Count, Is.GreaterThan(0), "the sales demo must open with a populated journal");
            Assert.That(feed.Count, Is.EqualTo(12), "the dashboard shows a full page of 12 entries");
            Assert.That(everything.Count, Is.GreaterThan(20), "many demo actions are audited");

            // Every attributed entry carries the demo company — nothing leaked with a wrong id...
            Assert.That(everything.All(e => e.CompanyId == companyId), Is.True);

            // ...and NOTHING was left with a NULL company (which would be invisible in the journal).
            using (IUnitOfWork uow = _uowf.Create())
            {
                int orphaned = uow.Audit.GetRecent(100000).Count(e => e.CompanyId == null);
                Assert.That(orphaned, Is.EqualTo(0), "no seed entry was written without a company");
            }
        }

        [Test]
        public void Demo_WithoutTheHook_WouldLeaveTheJournalEmpty()
        {
            // Control: this is the BUG the fix closes. With no company activated during seeding,
            // every audited action is written with CompanyId NULL and the journal shows nothing.
            var seeder = NewSeeder(); // CompanyActivated NOT wired → _activeCompany stays 0

            Result<long> result = seeder.Seed();
            Assert.That(result.IsSuccess, Is.True, result.Error);
            long companyId = result.Value;

            Assert.That(_audit.GetRecentForCompany(companyId, 12).Count, Is.EqualTo(0),
                "without the fix the demo journal is empty — the exact defect being corrected");

            using (IUnitOfWork uow = _uowf.Create())
            {
                Assert.That(uow.Audit.GetRecent(100000).Count(e => e.CompanyId == null), Is.GreaterThan(0),
                    "unattributed entries exist only in this control scenario");
            }
        }
    }
}
