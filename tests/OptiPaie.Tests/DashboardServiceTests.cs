using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The executive dashboard aggregation. Seeds one company with real data across
    /// several modules (a CDD expiring soon, a pending leave, an active loan, attendance
    /// today, an open posting, an assigned asset, a planned training) and asserts the
    /// company-wide snapshot rolls it all up — through the real services, on real SQLite.
    /// </summary>
    [TestFixture]
    public sealed class DashboardServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uowFactory;
        private IDashboardService _dashboard;

        private ICompanyService _companies;
        private IEmployeeService _employees;
        private IContractService _contracts;
        private ILeaveService _leave;
        private ILoanService _loans;
        private IAttendanceService _attendance;
        private IAtsService _ats;
        private IAssetService _assets;
        private ITrainingService _training;

        private long _companyId;
        private long _employeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-dash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _uowFactory = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uowFactory, new CompanyValidator());
            _employees = new EmployeeService(_uowFactory, new EmployeeValidator());
            _contracts = new ContractService(_uowFactory);
            _attendance = new AttendanceService(_uowFactory);
            _leave = new LeaveService(_uowFactory);
            _loans = new LoanService(_uowFactory);
            _ats = new AtsService(_uowFactory, new OptiPaie.Services.Validation.EmployeeValidator());
            _assets = new AssetService(_uowFactory);
            _training = new TrainingService(_uowFactory);

            _dashboard = new DashboardService(
                _companies, _employees, _contracts, _leave, _loans, _attendance, _ats, _assets, _training);

            Seed();
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* WAL still held */ }
        }

        private void Seed()
        {
            _companyId = _companies.Create(new Company { NameFr = "SARL Test", Nif = "000000000000000" }).Value;
            _employeeId = _employees.Create(new Employee
            {
                CompanyId = _companyId,
                LastNameFr = "BENALI",
                FirstNameFr = "Karim",
                Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdd,
                HireDate = new DateTime(2022, 1, 1),
                BaseSalary = 60000m,
                IsActive = true
            }).Value;

            // Active CDD expiring in ~15 days.
            long contract = _contracts.Save(new EmploymentContract
            {
                EmployeeId = _employeeId,
                Type = ContractType.Cdd,
                BaseSalary = 60000m,
                Position = "Comptable",
                StartDate = DateTime.Today.AddMonths(-6),
                EndDate = DateTime.Today.AddDays(15),
                TrialPeriodDays = 0
            }).Value;
            _contracts.Activate(contract);

            // A pending leave request (future working days).
            DateTime start = NextSunday();
            _leave.Save(new LeaveRequest
            {
                EmployeeId = _employeeId,
                Type = LeaveType.Annual,
                StartDate = start,
                EndDate = start.AddDays(2)
            });

            // An active loan.
            _loans.Save(new Loan
            {
                EmployeeId = _employeeId,
                Type = LoanType.Loan,
                Principal = 100000m,
                MonthlyInstallment = 20000m,
                StartYear = DateTime.Today.Year,
                StartMonth = DateTime.Today.Month
            });

            // Attendance today = present.
            _attendance.SetDayStatus(_employeeId, DateTime.Today, AttendanceStatus.Present);

            // An open posting with a candidate.
            long posting = _ats.SavePosting(new JobPosting
            {
                CompanyId = _companyId, Title = "Magasinier", Department = "Logistique", OpenDate = DateTime.Today, Positions = 1
            }).Value;
            _ats.SaveCandidate(new Candidate { PostingId = posting, LastName = "ZIANE", FirstName = "Ali", Phone = "0555111222" });

            // An assigned asset.
            long asset = _assets.Save(new Asset
            {
                CompanyId = _companyId, Name = "Ordinateur", Category = AssetCategory.Laptop, PurchaseValue = 90000m
            }).Value;
            _assets.Assign(asset, _employeeId, DateTime.Today, "Neuf", null);

            // A planned training session.
            _training.Save(new TrainingSession
            {
                CompanyId = _companyId, Title = "Sécurité", StartDate = DateTime.Today.AddDays(7)
            });
        }

        private static DateTime NextSunday()
        {
            var d = DateTime.Today.AddDays(7);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }

        // ------------------------------------------------------------------

        [Test]
        public void Build_RollsUpEveryModule()
        {
            DashboardSnapshot s = _dashboard.Build(_companyId, 30);

            Assert.That(s.Employees, Is.EqualTo(1));
            Assert.That(s.ActiveContracts, Is.EqualTo(1));
            Assert.That(s.ContractsExpiringSoon, Is.EqualTo(1), "the CDD ends within the 30-day window");
            Assert.That(s.PendingLeave, Is.EqualTo(1));
            Assert.That(s.ActiveLoans, Is.EqualTo(1));
            Assert.That(s.LoanOutstanding, Is.EqualTo(100000m));
            Assert.That(s.PresentToday, Is.EqualTo(1));
            Assert.That(s.OpenPostings, Is.EqualTo(1));
            Assert.That(s.Candidates, Is.EqualTo(1));
            Assert.That(s.AssetsAssigned, Is.EqualTo(1));
            Assert.That(s.TrainingUpcoming, Is.EqualTo(1));
        }

        [Test]
        public void Build_ProducesTheDeadlinesAndApprovalsQueues()
        {
            DashboardSnapshot s = _dashboard.Build(_companyId, 30);

            Assert.That(s.Deadlines.Count, Is.EqualTo(1));
            Assert.That(s.Deadlines[0].Kind, Is.EqualTo("contract"));
            Assert.That(s.Deadlines[0].Title, Does.Contain("BENALI"));

            Assert.That(s.Approvals.Count, Is.EqualTo(1));
            Assert.That(s.Approvals[0].Kind, Is.EqualTo("leave"));
            Assert.That(s.Approvals[0].Title, Does.Contain("BENALI"), "the approval carries the shared employee name");
        }

        [Test]
        public void Build_ExpiryWindow_ExcludesFarContracts()
        {
            // A 5-day window: the CDD (15 days out) is no longer "expiring soon".
            DashboardSnapshot s = _dashboard.Build(_companyId, 5);

            Assert.That(s.ContractsExpiringSoon, Is.EqualTo(0));
            Assert.That(s.Deadlines.Count, Is.EqualTo(0));
            Assert.That(s.ActiveContracts, Is.EqualTo(1), "the contract is still active, just not expiring within 5 days");
        }

        [Test]
        public void Build_AfterApproval_NoLongerPending()
        {
            long leaveId = _leave.GetByEmployee(_employeeId).First().Id;
            _leave.Approve(leaveId, null);

            DashboardSnapshot s = _dashboard.Build(_companyId, 30);

            Assert.That(s.PendingLeave, Is.EqualTo(0));
            Assert.That(s.Approvals.Count, Is.EqualTo(0), "an approved request leaves the queue");
        }

        // ------------------------------------------------------------------
        // Company isolation (mandatory tests #1 and #2).

        [Test]
        public void Build_WithoutACompany_Throws()
        {
            // The dashboard is strictly single-company: an all-companies total would leak
            // one client's data into another's. Calling without a valid company must fail.
            Assert.That(() => _dashboard.Build(0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _dashboard.Build(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Build_IsScopedToTheActiveCompany_NeverTheSumOfTwo()
        {
            // A SECOND company, populated differently from the first (2 employees, 2 open
            // postings, 3 candidates, no loans/leave/assets/training/attendance).
            long companyB = _companies.Create(new Company { NameFr = "SARL Autre", Nif = "111111111111111" }).Value;

            long empB1 = _employees.Create(new Employee
            {
                CompanyId = companyB, LastNameFr = "HADDAD", FirstNameFr = "Nabil",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2021, 3, 1), BaseSalary = 80000m, IsActive = true
            }).Value;
            _employees.Create(new Employee
            {
                CompanyId = companyB, LastNameFr = "MEZIANE", FirstNameFr = "Sara",
                Gender = Gender.Female, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2023, 6, 1), BaseSalary = 70000m, IsActive = true
            });

            long postingB1 = _ats.SavePosting(new JobPosting
            {
                CompanyId = companyB, Title = "Chauffeur", Department = "Transport", OpenDate = DateTime.Today, Positions = 1
            }).Value;
            long postingB2 = _ats.SavePosting(new JobPosting
            {
                CompanyId = companyB, Title = "Agent", Department = "Administration", OpenDate = DateTime.Today, Positions = 1
            }).Value;
            _ats.SaveCandidate(new Candidate { PostingId = postingB1, LastName = "A", FirstName = "A", Phone = "0555000001" });
            _ats.SaveCandidate(new Candidate { PostingId = postingB1, LastName = "B", FirstName = "B", Phone = "0555000002" });
            _ats.SaveCandidate(new Candidate { PostingId = postingB2, LastName = "C", FirstName = "C", Phone = "0555000003" });

            DashboardSnapshot a = _dashboard.Build(_companyId, 30);
            DashboardSnapshot b = _dashboard.Build(companyB, 30);

            // Each company reports ITS OWN numbers — different, and never the sum of both.
            Assert.That(a.Employees, Is.EqualTo(1));
            Assert.That(b.Employees, Is.EqualTo(2));
            Assert.That(a.Employees, Is.Not.EqualTo(b.Employees));
            Assert.That(b.Employees, Is.Not.EqualTo(a.Employees + b.Employees), "never the cross-company sum (3)");

            Assert.That(a.OpenPostings, Is.EqualTo(1));
            Assert.That(b.OpenPostings, Is.EqualTo(2));
            Assert.That(a.Candidates, Is.EqualTo(1));
            Assert.That(b.Candidates, Is.EqualTo(3));

            // Company A's exclusive data never bleeds into B's snapshot.
            Assert.That(b.ActiveLoans, Is.EqualTo(0), "company B has no loan; company A's must not appear");
            Assert.That(b.PendingLeave, Is.EqualTo(0), "company B has no leave; company A's must not appear");
            Assert.That(b.AssetsAssigned, Is.EqualTo(0));
            Assert.That(b.TrainingUpcoming, Is.EqualTo(0));
            Assert.That(b.PresentToday, Is.EqualTo(0));
        }
    }
}
