using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Common.Logging;
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
    /// Audit ACCOUNTABILITY: every recorded action is attributed to WHO performed it (the
    /// signed-in user, resolved at record time), and the coverage now includes salary changes,
    /// the employee lifecycle, user management and the login gate — not just leave/contracts.
    /// </summary>
    [TestFixture]
    public sealed class AuditAccountabilityTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private AuditService _audit;
        private string _actor;                // the controllable "current user"
        private ICompanyService _companies;
        private IEmployeeService _employees;
        private IPayrollElementService _elements;
        private IUserService _users;
        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-audit-acc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _audit = new AuditService(_uow, new SilentLogger());       // fallback actor = "Utilisateur"
            _audit.ActorProvider = () => _actor;                       // driven by the test

            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator()) { Audit = _audit };
            _elements = new PayrollElementService(_uow, new PayrollElementValidator());
            _users = new UserService(_uow, new SettingsService(_uow)) { Audit = _audit };

            _companyId = _companies.Create(new Company { NameFr = "SARL Audit", Nif = "000000000000000" }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // -- WHO performed it -------------------------------------------------

        [Test]
        public void Audit_RecordsTheCurrentUser_FromTheProvider()
        {
            _actor = "nadia";
            _audit.Record("Test", 1, AuditAction.Created, "x");
            Assert.That(_audit.GetRecent(1)[0].Actor, Is.EqualTo("nadia"));
        }

        [Test]
        public void Audit_FallsBackToGenericActor_WhenNoUserOrProviderFails()
        {
            _actor = null;
            _audit.Record("Test", 1, AuditAction.Created, "a");
            Assert.That(_audit.GetRecent(1)[0].Actor, Is.EqualTo("Utilisateur"));

            _audit.ActorProvider = () => "   ";                        // blank -> fallback
            _audit.Record("Test", 2, AuditAction.Created, "b");
            Assert.That(_audit.GetRecent(1)[0].Actor, Is.EqualTo("Utilisateur"));

            _audit.ActorProvider = () => throw new InvalidOperationException("boom"); // throws -> fallback
            _audit.Record("Test", 3, AuditAction.Created, "c");
            Assert.That(_audit.GetRecent(1)[0].Actor, Is.EqualTo("Utilisateur"));
        }

        // -- coverage: employee lifecycle ------------------------------------

        [Test]
        public void EmployeeDelete_IsAudited_WithTheActor()
        {
            _actor = "nadia";
            long empId = NewEmployee("Benali");

            Assert.That(_employees.Delete(empId).IsSuccess, Is.True);

            AuditEntry entry = _audit.GetForEntity("Employee", empId)
                .FirstOrDefault(e => e.Action == AuditAction.Deleted);
            Assert.That(entry, Is.Not.Null, "employee deletion must be audited");
            Assert.That(entry.Actor, Is.EqualTo("nadia"));
            Assert.That(entry.Summary, Does.Contain("Benali"));
        }

        // -- coverage: salary change -----------------------------------------

        [Test]
        public void SalaryChange_IsAudited_WithOldNewAndActor()
        {
            _actor = "nadia";
            long empId = NewEmployee("Salaire");
            long elementId = _elements.GetAll().First().Id;   // a default seeded rubric

            long assignmentId = _employees.AssignElement(new EmployeeElement
            {
                EmployeeId = empId, ElementId = elementId, Amount = 12000m, IsActive = true
            }).Value;

            AuditEntry created = _audit.GetForEntity("Salary", empId)
                .FirstOrDefault(e => e.Action == AuditAction.Created);
            Assert.That(created, Is.Not.Null, "a salary assignment must be audited");
            Assert.That(created.Actor, Is.EqualTo("nadia"));
            Assert.That(created.NewValue, Does.Contain("12000"));

            // A change records the old -> new amount.
            _employees.UpdateElement(new EmployeeElement { Id = assignmentId, EmployeeId = empId, ElementId = elementId, Amount = 15000m, IsActive = true });
            AuditEntry updated = _audit.GetForEntity("Salary", empId)
                .FirstOrDefault(e => e.Action == AuditAction.Updated);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated.OldValue, Does.Contain("12000"));
            Assert.That(updated.NewValue, Does.Contain("15000"));
        }

        // -- coverage: user management + login gate ("un paramètre") ---------

        [Test]
        public void UserManagement_AndLoginGate_AreAudited()
        {
            _actor = "boss@corp.dz";
            long a1 = _users.Create("admin", "Admin", "adminpass", UserRole.Admin, null).Value;
            _users.Create("admin2", "Admin 2", "adminpas2", UserRole.Admin, null); // 2nd admin so a1 may go

            Assert.That(_users.Delete(a1).IsSuccess, Is.True);
            _users.SetLoginEnabled(true);

            AuditEntry userDeleted = _audit.GetForEntity("User", a1)
                .FirstOrDefault(e => e.Action == AuditAction.Deleted);
            Assert.That(userDeleted, Is.Not.Null, "deleting a user must be audited");
            Assert.That(userDeleted.Actor, Is.EqualTo("boss@corp.dz"));

            AuditEntry gate = _audit.GetRecent(20)
                .FirstOrDefault(e => e.EntityType == "Settings" && e.Summary.Contains("Connexion au démarrage"));
            Assert.That(gate, Is.Not.Null, "enabling the login gate must be audited");
            Assert.That(gate.Actor, Is.EqualTo("boss@corp.dz"));
        }

        // -- helpers ----------------------------------------------------------

        private long NewEmployee(string lastName)
        {
            return _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = lastName, FirstNameFr = "Karim", Poste = "Agent",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(DateTime.Today.Year - 2, 1, 1),
                BaseSalary = 40000m, IsActive = true
            }).Value;
        }

        private sealed class SilentLogger : ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }
}
