using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 4 — authorization is enforced in the SERVICE layer, not by hiding buttons. Proves the
    /// pure role×permission matrix and that each of the five roles is refused (AccessDeniedException)
    /// when it calls a service operation it may not perform, and allowed when it may — and that with
    /// the login gate OFF everything is permitted (existing installs unaffected).
    /// </summary>
    [TestFixture]
    public sealed class AuthorizationTests
    {
        private string _dir;
        private UnitOfWorkFactory _uow;
        private FakeAccess _access;
        private CompanyService _companies;
        private EmployeeService _employees;
        private PayrollService _payroll;
        private LoanService _loans;
        private LeaveService _leave;
        private ContractService _contracts;
        private AttendanceService _attendance;
        private PayrollElementService _elements;
        private PerformanceService _performance;
        private TrainingService _training;
        private AssetService _assets;
        private WorkCertificateService _certificates;
        private AtsService _ats;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-authz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
            _access = new FakeAccess { IsEnforced = true, CurrentRole = UserRole.Admin };
            _companies = new CompanyService(_uow, new CompanyValidator(), _access);
            _employees = new EmployeeService(_uow, new EmployeeValidator(), _access);
            _payroll = new PayrollService(_uow, new ConfigurationService(_uow), new PayrollCalculationEngine(), _access);
            _loans = new LoanService(_uow, _access);
            _leave = new LeaveService(_uow, _access);
            _contracts = new ContractService(_uow, _access);
            _attendance = new AttendanceService(_uow, _access);
            _elements = new PayrollElementService(_uow, new PayrollElementValidator(), _access);
            _performance = new PerformanceService(_uow, _access);
            _training = new TrainingService(_uow, _access);
            _assets = new AssetService(_uow, _access);
            _certificates = new WorkCertificateService(_uow, _access);
            _ats = new AtsService(_uow, new EmployeeValidator(), _access);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ---- pure policy matrix -------------------------------------------------

        [Test]
        public void Policy_Admin_MayDoEverything_ExceptOwnerOnly()
        {
            foreach (Permission p in Enum.GetValues(typeof(Permission)))
            {
                // RestoreDatabase is owner-only (checked via IsOwner), never granted by any role.
                bool expected = p != Permission.RestoreDatabase;
                Assert.That(AccessPolicy.IsAllowed(UserRole.Admin, p), Is.EqualTo(expected),
                    "admin vs " + p);
            }
        }

        [Test]
        public void Restore_IsOwnerOnly_NotEvenAdminByRole()
        {
            // No role grants RestoreDatabase.
            foreach (UserRole r in Enum.GetValues(typeof(UserRole)))
            {
                Assert.That(AccessPolicy.IsAllowed(r, Permission.RestoreDatabase), Is.False, r + " must not hold RestoreDatabase");
            }
            // RequireOwner: a null context fails closed; an admin USER (not owner) is denied; the owner passes.
            Assert.Throws<AccessDeniedException>(() => AccessPolicy.RequireOwner(null));
            Assert.Throws<AccessDeniedException>(() => AccessPolicy.RequireOwner(new FakeAccess { IsEnforced = true, CurrentRole = UserRole.Admin, IsOwner = false }));
            Assert.DoesNotThrow(() => AccessPolicy.RequireOwner(new FakeAccess { IsEnforced = true, IsOwner = true }));
            Assert.DoesNotThrow(() => AccessPolicy.RequireOwner(new FakeAccess { IsEnforced = false })); // open install
        }

        [Test]
        public void Policy_Administration_IsAdminOnly()
        {
            foreach (UserRole r in new[] { UserRole.Hr, UserRole.Payroll, UserRole.Accountant, UserRole.ReadOnly })
            {
                Assert.That(AccessPolicy.IsAllowed(r, Permission.ManageCompanies), Is.False);
                Assert.That(AccessPolicy.IsAllowed(r, Permission.ManageSettings), Is.False);
                Assert.That(AccessPolicy.IsAllowed(r, Permission.ManageUsers), Is.False);
            }
        }

        [Test]
        public void Policy_DomainsMapToTheRightRoles()
        {
            Assert.That(AccessPolicy.IsAllowed(UserRole.Hr, Permission.EditEmployees), Is.True);
            Assert.That(AccessPolicy.IsAllowed(UserRole.Payroll, Permission.EditEmployees), Is.False);

            Assert.That(AccessPolicy.IsAllowed(UserRole.Payroll, Permission.RunPayroll), Is.True);
            Assert.That(AccessPolicy.IsAllowed(UserRole.Hr, Permission.RunPayroll), Is.False);

            // Attendance is shared by HR and payroll.
            Assert.That(AccessPolicy.IsAllowed(UserRole.Hr, Permission.ManageAttendance), Is.True);
            Assert.That(AccessPolicy.IsAllowed(UserRole.Payroll, Permission.ManageAttendance), Is.True);

            // Accountant and read-only never WRITE. (ViewSalary is a READ permission — the one
            // door-independent salary gate — so it is excluded here and covered by its own sweep;
            // the accountant legitimately holds it, read-only does not.)
            foreach (Permission p in Enum.GetValues(typeof(Permission)))
            {
                if (p == Permission.ViewSalary) continue;
                Assert.That(AccessPolicy.IsAllowed(UserRole.Accountant, p), Is.False, "accountant must not write: " + p);
                Assert.That(AccessPolicy.IsAllowed(UserRole.ReadOnly, p), Is.False, "read-only must not write: " + p);
            }

            // The salary gate: every role except read-only may see pay.
            Assert.That(AccessPolicy.IsAllowed(UserRole.Accountant, Permission.ViewSalary), Is.True);
            Assert.That(AccessPolicy.IsAllowed(UserRole.ReadOnly, Permission.ViewSalary), Is.False);
        }

        // ---- service-layer enforcement, per role --------------------------------

        [Test]
        public void ReadOnly_IsRefused_EveryWrite()
        {
            _access.CurrentRole = UserRole.ReadOnly;
            AssertDenied(() => _companies.Create(new Company { NameFr = "X" }));
            AssertDenied(() => _employees.Create(new Employee()));
            AssertDenied(() => _payroll.Generate(new PayrollGenerationRequest()));
            AssertDenied(() => _loans.Save(new Loan()));
            AssertDenied(() => _leave.Save(new LeaveRequest()));
            AssertDenied(() => _contracts.Save(new EmploymentContract()));
            AssertDenied(() => _attendance.Save(new AttendanceRecord()));
        }

        [Test]
        public void Accountant_IsRefused_EveryWrite()
        {
            _access.CurrentRole = UserRole.Accountant;
            AssertDenied(() => _companies.Create(new Company { NameFr = "X" }));
            AssertDenied(() => _employees.Create(new Employee()));
            AssertDenied(() => _payroll.Generate(new PayrollGenerationRequest()));
            AssertDenied(() => _loans.Save(new Loan()));
        }

        [Test]
        public void Hr_MayEditHr_ButNotPayrollOrAdmin()
        {
            _access.CurrentRole = UserRole.Hr;
            AssertAllowed(() => _employees.Create(new Employee()));      // HR domain
            AssertAllowed(() => _leave.Save(new LeaveRequest()));
            AssertAllowed(() => _contracts.Save(new EmploymentContract()));
            AssertAllowed(() => _attendance.Save(new AttendanceRecord()));
            AssertDenied(() => _payroll.Generate(new PayrollGenerationRequest())); // payroll domain
            AssertDenied(() => _loans.Save(new Loan()));
            AssertDenied(() => _companies.Create(new Company { NameFr = "X" })); // admin
        }

        [Test]
        public void Payroll_MayRunPayrollAndLoans_ButNotEmployeesOrAdmin()
        {
            _access.CurrentRole = UserRole.Payroll;
            AssertAllowed(() => _payroll.Generate(new PayrollGenerationRequest()));
            AssertAllowed(() => _loans.Save(new Loan()));
            AssertAllowed(() => _attendance.Save(new AttendanceRecord())); // shared
            AssertDenied(() => _employees.Create(new Employee()));         // HR domain
            AssertDenied(() => _leave.Save(new LeaveRequest()));
            AssertDenied(() => _companies.Create(new Company { NameFr = "X" })); // admin
        }

        [Test]
        public void Admin_IsAllowed_Everywhere()
        {
            _access.CurrentRole = UserRole.Admin;
            AssertAllowed(() => _companies.Create(new Company { NameFr = "X" }));
            AssertAllowed(() => _employees.Create(new Employee()));
            AssertAllowed(() => _payroll.Generate(new PayrollGenerationRequest()));
            AssertAllowed(() => _loans.Save(new Loan()));
        }

        [Test]
        public void GateOff_EverythingAllowed_RegardlessOfRole()
        {
            _access.IsEnforced = false;
            _access.CurrentRole = UserRole.ReadOnly; // would be denied if enforced
            AssertAllowed(() => _companies.Create(new Company { NameFr = "X" }));
            AssertAllowed(() => _payroll.Generate(new PayrollGenerationRequest()));
            AssertAllowed(() => _employees.Create(new Employee()));
        }

        [Test]
        public void DomainServices_EnforceTheirOwnPermission()
        {
            // Read-only is refused on every domain write.
            _access.CurrentRole = UserRole.ReadOnly;
            AssertDenied(() => _elements.Create(new PayrollElement()));
            AssertDenied(() => _performance.SavePeriod(new EvalPeriod()));
            AssertDenied(() => _training.Save(new TrainingSession()));
            AssertDenied(() => _assets.Save(new Asset()));
            AssertDenied(() => _certificates.Save(new WorkCertificate()));
            AssertDenied(() => _ats.SavePosting(new JobPosting()));

            // HR owns performance/training/assets/certificates/recruitment, NOT payroll elements.
            _access.CurrentRole = UserRole.Hr;
            AssertAllowed(() => _performance.SavePeriod(new EvalPeriod()));
            AssertAllowed(() => _training.Save(new TrainingSession()));
            AssertAllowed(() => _assets.Save(new Asset()));
            AssertAllowed(() => _certificates.Save(new WorkCertificate()));
            AssertAllowed(() => _ats.SavePosting(new JobPosting()));
            AssertDenied(() => _elements.Create(new PayrollElement()));

            // Payroll owns the payroll-element catalogue, NOT performance.
            _access.CurrentRole = UserRole.Payroll;
            AssertAllowed(() => _elements.Create(new PayrollElement()));
            AssertDenied(() => _performance.SavePeriod(new EvalPeriod()));
            AssertDenied(() => _training.Save(new TrainingSession()));
        }

        // A denied call throws AccessDeniedException (BEFORE any validation/persistence).
        private static void AssertDenied(TestDelegate action) => Assert.Throws<AccessDeniedException>(action);

        // An allowed call passes authorization: it may still fail validation (a Result), but it must
        // NOT throw AccessDeniedException.
        private static void AssertAllowed(Action action)
        {
            try { action(); }
            catch (AccessDeniedException) { Assert.Fail("authorization should have permitted this call"); }
            catch { /* other failures (validation/missing data) are irrelevant to authorization */ }
        }

        private sealed class FakeAccess : IAccessContext
        {
            public bool IsEnforced { get; set; }
            public UserRole CurrentRole { get; set; }
            public bool IsOwner { get; set; }
            public long? CurrentUserId { get; set; }
        }
    }
}
