using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Services;
using OptiPaie.Services.Auth;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 5 — after login, the audit trail carries the REAL "who". Drives the full chain:
    /// create a user, enable the gate, sign in through the real LoginCoordinator, then perform an
    /// audited action — and shows the actual stored AuditLog row naming that user. Prints the line.
    /// </summary>
    [TestFixture]
    public sealed class UsersScreenAuditProofTests
    {
        private string _dir;
        private UnitOfWorkFactory _uow;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-whoproof-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void AfterLogin_AuditTrail_CarriesTheRealUser()
        {
            var settings = new SettingsService(_uow);
            var users = new UserService(_uow, settings);               // gate off -> admin creation allowed
            var audit = new AuditService(_uow, NullLogger.Instance);

            // The signed-in user, resolved at record time (what SessionAccessContext/CompositionRoot wire).
            User signedIn = null;
            audit.ActorProvider = () => signedIn != null ? signedIn.Username : null;

            var companies = new CompanyService(_uow, new CompanyValidator());
            var employees = new EmployeeService(_uow, new EmployeeValidator()) { Audit = audit };

            long companyId = companies.Create(new Company { NameFr = "SARL Preuve", Nif = "000000000000000" }).Value;

            // 1. Create an admin and turn the login gate on.
            Assert.That(users.Create("nadia", "Nadia Zeroual", "motdepasse01", UserRole.Admin, null).IsSuccess, Is.True);
            users.SetLoginEnabled(true);

            // 2. Sign in through the real coordinator (owner=null -> local user path).
            var coordinator = new LoginCoordinator(null, users, new LoginThrottle(), _ => { });
            LoginOutcome outcome = coordinator.Login("nadia", "motdepasse01");
            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.User, Is.Not.Null);
            signedIn = outcome.User;                                   // seat the session

            // 3. Perform an audited action as that user.
            long empId = employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "Benali", FirstNameFr = "Karim",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = DateTime.Today.AddYears(-1), BaseSalary = 50000m, IsActive = true
            }).Value;

            // 4. The stored audit row names the real user.
            AuditEntry entry = audit.GetForEntity("Employee", empId).First(e => e.Action == AuditAction.Created);
            Assert.That(entry.Actor, Is.EqualTo("nadia"));

            TestContext.WriteLine("AUDIT LINE -> actor='" + entry.Actor + "' action=" + entry.Action +
                                  " entity=Employee#" + empId + " summary='" + entry.Summary + "'");
        }
    }
}
