using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Auditing;
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
    /// The audit trail. Proves the append-only store records and reads back, and — the
    /// point of the optional-sink design — that a module service wired to the sink records
    /// its lifecycle events (a leave approval) without any change to its constructor.
    /// </summary>
    [TestFixture]
    public sealed class AuditServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uow;
        private AuditService _audit;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-audit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _audit = new AuditService(_uow, new NullLogger(), "HR");
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        [Test]
        public void Record_ThenGetForEntity_ReturnsTheHistory()
        {
            _audit.Record("Contract", 5, AuditAction.StatusChanged, "Contrat activé", "Préparation", "En vigueur");
            _audit.Record("Contract", 5, AuditAction.StatusChanged, "Contrat résilié", "En vigueur", "Résilié");
            _audit.Record("Contract", 6, AuditAction.Created, "Autre contrat");

            var history = _audit.GetForEntity("Contract", 5);

            Assert.That(history.Count, Is.EqualTo(2), "only entity #5");
            Assert.That(history[0].Summary, Is.EqualTo("Contrat résilié"), "most recent first");
            Assert.That(history[0].OldValue, Is.EqualTo("En vigueur"));
            Assert.That(history[0].NewValue, Is.EqualTo("Résilié"));
            Assert.That(history[0].Actor, Is.EqualTo("HR"));
        }

        [Test]
        public void GetRecent_ReturnsAcrossEntities_NewestFirst()
        {
            _audit.Record("Leave", 1, AuditAction.Approved, "Congé approuvé");
            _audit.Record("Asset", 2, AuditAction.Assigned, "Matériel attribué");

            var recent = _audit.GetRecent(10);

            Assert.That(recent.Count, Is.EqualTo(2));
            Assert.That(recent[0].EntityType, Is.EqualTo("Asset"), "the latest action is first");
        }

        [Test]
        public void LeaveApproval_WiredToTheSink_RecordsHistory_WithoutCtorChange()
        {
            var companies = new CompanyService(_uow, new CompanyValidator());
            var employees = new EmployeeService(_uow, new EmployeeValidator());
            var leave = new LeaveService(_uow) { Audit = _audit }; // optional property, not a ctor arg

            long companyId = companies.Create(new Company { NameFr = "SARL Test", Nif = "000000000000000" }).Value;
            long employeeId = employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "BENALI", FirstNameFr = "Karim",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2022, 1, 1), BaseSalary = 60000m, IsActive = true
            }).Value;

            DateTime start = NextSunday();
            long leaveId = leave.Save(new LeaveRequest { EmployeeId = employeeId, Type = LeaveType.Annual, StartDate = start, EndDate = start.AddDays(1) }).Value;
            leave.Approve(leaveId, null);

            var history = _audit.GetForEntity("Leave", leaveId);
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Action, Is.EqualTo(AuditAction.Approved));
            Assert.That(history[0].NewValue, Is.EqualTo("Approuvé"));
        }

        [Test]
        public void UnwiredService_UsesNoOpSink_AndDoesNotRecord()
        {
            var companies = new CompanyService(_uow, new CompanyValidator());
            var employees = new EmployeeService(_uow, new EmployeeValidator());
            var leave = new LeaveService(_uow); // no Audit set → NullAuditSink

            long companyId = companies.Create(new Company { NameFr = "SARL Test", Nif = "000000000000000" }).Value;
            long employeeId = employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "X", FirstNameFr = "Y",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2022, 1, 1), BaseSalary = 50000m, IsActive = true
            }).Value;
            DateTime start = NextSunday();
            long leaveId = leave.Save(new LeaveRequest { EmployeeId = employeeId, Type = LeaveType.Annual, StartDate = start, EndDate = start.AddDays(1) }).Value;
            leave.Approve(leaveId, null);

            Assert.That(_audit.GetForEntity("Leave", leaveId).Count, Is.EqualTo(0), "no sink wired → nothing recorded, no crash");
        }

        private static DateTime NextSunday()
        {
            var d = DateTime.Today.AddDays(7);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }

        private sealed class NullLogger : OptiPaie.Common.Logging.ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }
}
