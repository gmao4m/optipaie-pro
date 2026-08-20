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
            Assert.That(history.Any(e => e.Action == AuditAction.Approved && e.NewValue == "Approuvé"), Is.True,
                "the approval is recorded in the audit trail");
            Assert.That(history.Any(e => e.Action == AuditAction.Created), Is.True,
                "creating the request is audited too (journal à chaque transition)");
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

        // ------------------------------------------------------------------
        // Company isolation of the activity journal (mandatory test #3).

        [Test]
        public void GetRecentForCompany_ShowsOnlyThatCompany_NeverAnother()
        {
            // The CompanyProvider is the single choke point: whatever is active at record time
            // tags the entry. Company 1 acts, then company 2 acts.
            long active = 1;
            _audit.CompanyProvider = () => active;

            active = 1;
            _audit.Record("Leave", 10, AuditAction.Approved, "Congé approuvé (société 1)");
            _audit.Record("Asset", 11, AuditAction.Assigned, "Matériel attribué (société 1)");

            active = 2;
            _audit.Record("Leave", 20, AuditAction.Approved, "Congé approuvé (société 2)");

            var journal1 = _audit.GetRecentForCompany(1, 50);
            var journal2 = _audit.GetRecentForCompany(2, 50);

            Assert.That(journal1.Count, Is.EqualTo(2), "society 1 sees its own two entries");
            Assert.That(journal1.All(e => e.CompanyId == 1), Is.True);
            Assert.That(journal1.Any(e => e.Summary.Contains("société 2")), Is.False,
                "society 2's action must NEVER appear in society 1's journal");

            Assert.That(journal2.Count, Is.EqualTo(1), "society 2 sees only its own entry");
            Assert.That(journal2.All(e => e.CompanyId == 2), Is.True);
            Assert.That(journal2.Any(e => e.Summary.Contains("société 1")), Is.False,
                "society 1's actions must NEVER appear in society 2's journal");
        }

        [Test]
        public void GetRecentForCompany_ExcludesLegacyNullEntries_ButKeepsThemInTheStore()
        {
            // No active company (pre-isolation / demo-seeding state) → CompanyId NULL.
            _audit.CompanyProvider = () => null;
            _audit.Record("Contract", 99, AuditAction.Created, "Ancienne entrée sans société");

            // The row is KEPT (visible to the all-companies query) ...
            Assert.That(_audit.GetRecent(50).Any(e => e.EntityId == 99 && e.CompanyId == null), Is.True,
                "the legacy NULL entry is preserved in the store");

            // ... but EXCLUDED from every per-company journal (never shown, never leaked).
            Assert.That(_audit.GetRecentForCompany(1, 50).Any(e => e.EntityId == 99), Is.False);
            Assert.That(_audit.GetRecentForCompany(2, 50).Any(e => e.EntityId == 99), Is.False);
        }

        [Test]
        public void GetRecentForCompany_WithoutACompany_Throws()
        {
            Assert.That(() => _audit.GetRecentForCompany(0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _audit.GetRecentForCompany(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Record_WithoutACompanyProvider_StoresNull_AndStaysOutOfJournals()
        {
            // No provider wired at all (e.g. a test or a headless path) — must not crash, and the
            // entry simply carries no company, so it never surfaces in a per-company journal.
            _audit.Record("User", 1, AuditAction.Created, "Utilisateur créé");

            Assert.That(_audit.GetRecent(10).Single().CompanyId, Is.Null);
            Assert.That(_audit.GetRecentForCompany(1, 10).Count, Is.EqualTo(0));
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
