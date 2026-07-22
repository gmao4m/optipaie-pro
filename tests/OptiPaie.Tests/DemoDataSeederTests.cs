using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Demo dataset — integration test against a real SQLite file. Runs the seeder through
    /// the real module services and asserts the whole interconnected dataset is coherent:
    /// one Algerian company, ~20 employees across 4 departments, contracts (incl. a
    /// near-expiry CDD and an active probation), a full month of attendance, leave in every
    /// state, a completed performance cycle with promotions/rewards, loans at different
    /// stages, assets incl. one under maintenance, training with certificates, and work
    /// certificates. Also proves it never overwrites an existing database.
    /// </summary>
    [TestFixture]
    public sealed class DemoDataSeederTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uowf;

        private ICompanyService _companies;
        private IEmployeeService _employees;
        private IContractService _contracts;
        private IAttendanceService _attendance;
        private ILeaveService _leave;
        private ILoanService _loans;
        private IAssetService _assets;
        private ITrainingService _training;
        private IWorkCertificateService _certificates;
        private IPerformanceService _performance;

        private DemoDataSeeder _seeder;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-demo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _uowf = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uowf, new CompanyValidator());
            _employees = new EmployeeService(_uowf, new EmployeeValidator());
            _contracts = new ContractService(_uowf);
            _attendance = new AttendanceService(_uowf);
            _leave = new LeaveService(_uowf);
            _loans = new LoanService(_uowf);
            _assets = new AssetService(_uowf);
            _training = new TrainingService(_uowf);
            _certificates = new WorkCertificateService(_uowf);
            _performance = new PerformanceService(_uowf, _attendance);

            _seeder = new DemoDataSeeder(_companies, _employees, _contracts, _attendance, _leave,
                _loans, _assets, _training, _certificates, _performance);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* WAL still held */ }
        }

        private long SeedOk()
        {
            Result<long> result = _seeder.Seed();
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(result.Value, Is.GreaterThan(0L));
            return result.Value;
        }

        [Test]
        public void Seed_CreatesTheAlgerianCompanyWithALogo()
        {
            long companyId = SeedOk();

            Company company = _companies.Get(companyId);
            Assert.That(company.NameFr, Is.EqualTo("SARL Atlas Industrie"));
            Assert.That(company.Nif, Is.Not.Empty);
            Assert.That(company.AddressFr, Does.Contain("Blida"));
            Assert.That(company.Logo, Is.Not.Null);
            Assert.That(company.Logo.Length, Is.GreaterThan(200), "a real PNG logo, not an empty box");
        }

        [Test]
        public void Seed_CreatesTwentyEmployeesAcrossFourDepartments()
        {
            long companyId = SeedOk();

            IReadOnlyList<Employee> employees = _employees.GetByCompany(companyId);
            Assert.That(employees.Count, Is.EqualTo(20));

            List<string> departments = employees.Select(e => e.Department).Distinct().OrderBy(d => d).ToList();
            Assert.That(departments, Is.EquivalentTo(new[] { "Administration", "Commercial", "Informatique", "Production" }));

            // Names look real (no placeholder junk).
            Assert.That(employees.Any(e => e.LastNameFr == "BENALI"), Is.True);
            Assert.That(employees.All(e => e.BaseSalary >= 38000m && e.BaseSalary <= 100000m), Is.True, "plausible salaries");
        }

        [Test]
        public void Seed_Contracts_IncludeANearExpiryCddAndAnActiveProbation()
        {
            long companyId = SeedOk();

            Assert.That(_contracts.GetByCompany(companyId).Count, Is.EqualTo(20), "one active contract per employee");

            IReadOnlyList<ContractSummary> expiring = _contracts.GetExpiring(companyId, 30);
            Assert.That(expiring.Count, Is.GreaterThanOrEqualTo(1), "the near-expiry CDD shows in the expiry alert");

            Employee rebai = _employees.GetByCompany(companyId).First(e => e.LastNameFr == "REBAI");
            IReadOnlyList<ContractSummary> rebaiContracts = _contracts.GetByEmployee(rebai.Id);
            Assert.That(rebaiContracts.Count, Is.GreaterThanOrEqualTo(1));
            EmploymentContract probation = _contracts.Get(rebaiContracts[0].ContractId);
            Assert.That(probation.TrialPeriodDays, Is.EqualTo(90), "an active période d'essai");
        }

        [Test]
        public void Seed_FillsAFullMonthOfAttendanceWithVariety()
        {
            long companyId = SeedOk();

            DateTime demoMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
            IReadOnlyList<AttendanceRecord> records = _attendance.GetCompanyMonth(companyId, demoMonth.Year, demoMonth.Month);

            Assert.That(records.Count, Is.GreaterThan(200), "a populated grid, not an empty one");
            List<AttendanceStatus> statuses = records.Select(r => r.Status).Distinct().ToList();
            Assert.That(statuses, Does.Contain(AttendanceStatus.Present));
            Assert.That(statuses.Any(s => s == AttendanceStatus.Absent || s == AttendanceStatus.Late || s == AttendanceStatus.Mission), Is.True);
        }

        [Test]
        public void Seed_LeaveCoversApprovedPendingAndRejected()
        {
            long companyId = SeedOk();

            var all = new List<LeaveRequest>();
            all.AddRange(_leave.GetByCompanyYear(companyId, DateTime.Today.Year));
            all.AddRange(_leave.GetByCompanyYear(companyId, DateTime.Today.Year - 1));

            Assert.That(all.Any(r => r.Status == LeaveStatus.Approved), Is.True);
            Assert.That(all.Any(r => r.Status == LeaveStatus.Pending), Is.True);
            Assert.That(all.Any(r => r.Status == LeaveStatus.Rejected), Is.True);
        }

        [Test]
        public void Seed_HasACompletedPerformanceCycleWithPromotionsAndRewards()
        {
            long companyId = SeedOk();

            IReadOnlyList<CycleSummary> cycles = _performance.GetCycles(companyId);
            Assert.That(cycles.Count, Is.GreaterThanOrEqualTo(1));
            CycleSummary cycle = cycles.First();
            Assert.That(cycle.CompletionPercent, Is.EqualTo(100m), "every review submitted");
            Assert.That(cycle.Status, Is.EqualTo(PerformanceCycleStatus.Completed));

            PerformanceDashboard dashboard = _performance.GetDashboard(companyId);
            Assert.That(dashboard.TopPerformers.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(dashboard.ReviewCount, Is.EqualTo(20));

            Employee touati = _employees.GetByCompany(companyId).First(e => e.LastNameFr == "TOUATI");
            CareerTimeline timeline = _performance.GetCareerTimeline(touati.Id);
            Assert.That(timeline.Items.Any(i => i.Kind == "reward"), Is.True, "a bonus on the career timeline");
            Assert.That(timeline.Items.Any(i => i.Kind == "review"), Is.True);
        }

        [Test]
        public void Seed_LoansAreAtDifferentRepaymentStages()
        {
            long companyId = SeedOk();

            IReadOnlyList<LoanSummary> loans = _loans.GetByCompany(companyId);
            Assert.That(loans.Count, Is.EqualTo(3));

            Employee bouzid = _employees.GetByCompany(companyId).First(e => e.LastNameFr == "BOUZID");
            decimal outstanding = _loans.GetOutstanding(bouzid.Id);
            Assert.That(outstanding, Is.LessThanOrEqualTo(20000m), "the 200 000 DA loan is nearly settled");
            Assert.That(outstanding, Is.GreaterThan(0m));
        }

        [Test]
        public void Seed_AssetsIncludeOneUnderMaintenance()
        {
            long companyId = SeedOk();

            IReadOnlyList<AssetSummary> assets = _assets.GetByCompany(companyId);
            Assert.That(assets.Count, Is.EqualTo(6));
            Assert.That(assets.Any(a => a.Status == AssetStatus.UnderRepair), Is.True, "one asset flagged for maintenance");
            Assert.That(assets.Count(a => a.Status == AssetStatus.Assigned), Is.GreaterThanOrEqualTo(4), "most assets are handed out");
        }

        [Test]
        public void Seed_TrainingAndWorkCertificatesExist()
        {
            long companyId = SeedOk();

            IReadOnlyList<TrainingSummary> sessions = _training.GetByCompany(companyId);
            Assert.That(sessions.Count, Is.EqualTo(2));
            Assert.That(sessions.Any(s => s.Status == TrainingStatus.Completed), Is.True);

            Employee amrani = _employees.GetByCompany(companyId).First(e => e.LastNameFr == "AMRANI");
            Assert.That(_certificates.GetByEmployee(amrani.Id).Count, Is.GreaterThanOrEqualTo(1), "a work certificate is on file");

            // The certificate assembles a real render model (the printable document).
            CertificateSummary cert = _certificates.GetByEmployee(amrani.Id).First();
            CertificateRenderModel render = _certificates.BuildRender(cert.CertificateId);
            Assert.That(render, Is.Not.Null, "the printed work certificate is generatable with real data");
        }

        [Test]
        public void Seed_IsIdempotent_NeverOverwritesAnExistingDatabase()
        {
            long first = SeedOk();

            Result<long> second = _seeder.Seed();
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(second.Value, Is.EqualTo(0L), "a non-empty database is left untouched");
            Assert.That(_companies.GetAll().Count, Is.EqualTo(1), "no duplicate company");
            Assert.That(first, Is.GreaterThan(0L));
        }
    }
}
