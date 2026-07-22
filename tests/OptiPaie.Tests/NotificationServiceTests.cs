using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The central notification engine. Seeds an expiring contract, a pending leave and a
    /// training starting this week, then asserts the ranked alert list surfaces them with
    /// the right severity and navigation target — through the real services on real SQLite.
    /// </summary>
    [TestFixture]
    public sealed class NotificationServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uow;
        private INotificationService _notifications;

        private ICompanyService _companies;
        private IEmployeeService _employees;
        private IContractService _contracts;
        private ILeaveService _leave;
        private ITrainingService _training;

        private long _companyId;
        private long _employeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-notif-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _contracts = new ContractService(_uow);
            _leave = new LeaveService(_uow);
            _training = new TrainingService(_uow);
            _notifications = new NotificationService(_companies, _employees, _contracts, _leave, _training,
                new PerformanceService(_uow, new AttendanceService(_uow)));

            _companyId = _companies.Create(new Company { NameFr = "SARL Test", Nif = "000000000000000" }).Value;
            _employeeId = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "BENALI", FirstNameFr = "Karim",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdd, HireDate = new DateTime(2022, 1, 1), BaseSalary = 60000m, IsActive = true
            }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        [Test]
        public void GetNotifications_SurfacesUrgentContractExpiry()
        {
            long c = _contracts.Save(new EmploymentContract
            {
                EmployeeId = _employeeId, Type = ContractType.Cdd, BaseSalary = 60000m, Position = "X",
                StartDate = DateTime.Today.AddMonths(-6), EndDate = DateTime.Today.AddDays(3)
            }).Value;
            _contracts.Activate(c);

            var n = _notifications.GetNotifications(30);

            var contract = n.FirstOrDefault(x => x.Kind == "contract");
            Assert.That(contract, Is.Not.Null);
            Assert.That(contract.Severity, Is.EqualTo(NotificationSeverity.Urgent), "ending in 3 days is urgent");
            Assert.That(contract.ModuleKey, Is.EqualTo(ModuleKeys.Contracts));
            Assert.That(contract.Title, Does.Contain("BENALI"));
        }

        [Test]
        public void GetNotifications_SurfacesPendingLeaveAndUpcomingTraining()
        {
            DateTime start = NextSunday();
            _leave.Save(new LeaveRequest { EmployeeId = _employeeId, Type = LeaveType.Annual, StartDate = start, EndDate = start.AddDays(1) });
            _training.Save(new TrainingSession { CompanyId = _companyId, Title = "Sécurité", StartDate = DateTime.Today.AddDays(3) });

            var n = _notifications.GetNotifications(30);

            Assert.That(n.Any(x => x.Kind == "leave" && x.Severity == NotificationSeverity.Warning), Is.True);
            Assert.That(n.Any(x => x.Kind == "training" && x.ModuleKey == ModuleKeys.Training), Is.True);
        }

        [Test]
        public void GetNotifications_RanksUrgentFirst()
        {
            long c = _contracts.Save(new EmploymentContract
            {
                EmployeeId = _employeeId, Type = ContractType.Cdd, BaseSalary = 60000m, Position = "X",
                StartDate = DateTime.Today.AddMonths(-6), EndDate = DateTime.Today.AddDays(2)
            }).Value;
            _contracts.Activate(c);
            _training.Save(new TrainingSession { CompanyId = _companyId, Title = "Info", StartDate = DateTime.Today.AddDays(5) });

            var n = _notifications.GetNotifications(30);

            Assert.That(n.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(n[0].Severity, Is.EqualTo(NotificationSeverity.Urgent), "the most urgent alert is first");
        }

        [Test]
        public void GetNotifications_QuietWhenNothingIsDue()
        {
            Assert.That(_notifications.GetNotifications(30).Count, Is.EqualTo(0));
        }

        private static DateTime NextSunday()
        {
            var d = DateTime.Today.AddDays(7);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }
    }
}
