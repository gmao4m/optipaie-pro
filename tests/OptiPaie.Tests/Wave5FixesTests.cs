using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Documents;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>Vague 5 (consolidation) — service-layer regression tests. Each fails if the defect returns.</summary>
    [TestFixture]
    public sealed class Wave5FixesTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uowf;
        private AttendanceService _attendance;
        private LeaveService _leave;
        private LoanService _loans;
        private long _companyId, _employeeId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-wave5-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uowf = new UnitOfWorkFactory(factory);
            _attendance = new AttendanceService(_uowf);
            _leave = new LeaveService(_uowf);
            _loans = new LoanService(_uowf);

            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId, LastNameFr = "BENALI", FirstNameFr = "Karim",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, BaseSalary = 40000m, HireDate = new DateTime(2020, 1, 1), IsActive = true
                });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ── #27 : a matrix-painted "Retard" (LateMinutes=0) is counted in the monthly summary ──
        [Test]
        public void MatrixLate_IsCountedInMonthlySummary()
        {
            Assert.That(_attendance.SetDayStatus(_employeeId, new DateTime(2026, 3, 6), AttendanceStatus.Late).IsSuccess, Is.True);
            var s = _attendance.GetMonthlySummary(_employeeId, 2026, 3);
            Assert.That(s.LateCount, Is.EqualTo(1), "a painted 'R' day (Status=Late, LateMinutes=0) must count as a retard");
            Assert.That(s.PresentDays, Is.EqualTo(1), "and still counts as a present/worked day");
        }

        // ── #29 : PaidDays mirrors the payroll gate (monthDays − AbsentDays when recorded, else full month) ──
        [Test]
        public void PaidDays_MatchesPayroll_MonthMinusAbsences()
        {
            int monthDays = DateTime.DaysInMonth(2026, 3);
            _attendance.SetDayStatus(_employeeId, new DateTime(2026, 3, 4), AttendanceStatus.Absent);
            _attendance.SetDayStatus(_employeeId, new DateTime(2026, 3, 5), AttendanceStatus.Absent);

            Assert.That(_attendance.GetMonthlySummary(_employeeId, 2026, 3).PaidDays, Is.EqualTo(monthDays - 2),
                "with recorded days, paid = month − absences (the engine's workedDays)");

            long other = InsertEmployee("HADDAD", "Sofiane");
            Assert.That(_attendance.GetMonthlySummary(other, 2026, 3).PaidDays, Is.EqualTo(monthDays),
                "with no record, the whole month is paid");
        }

        // ── #32 : a OncePerCareer leave type cannot be taken twice ──
        [Test]
        public void OncePerCareerLeaveType_CannotBeTakenTwice()
        {
            long typeId;
            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                typeId = uow.LeaveTypes.Insert(new LeaveTypeDefinition
                {
                    CompanyId = _companyId, Code = "PILGRIMAGE_TEST", LabelAr = "الحج", LabelFr = "Pèlerinage",
                    BaseType = LeaveType.Special, PaymentCategory = PaymentCategory.EmployerPaid,
                    DecrementsAnnualBalance = false, OncePerCareer = true, IsActive = true
                });
                uow.Commit();
            }

            LeaveRequest First() => new LeaveRequest
            {
                EmployeeId = _employeeId, Type = LeaveType.Special, LeaveTypeId = typeId,
                StartDate = new DateTime(2026, 3, 2), EndDate = new DateTime(2026, 3, 3), IsDraft = false
            };
            Assert.That(_leave.Save(First()).IsSuccess, Is.True, "the first pilgrimage leave is allowed");

            var second = new LeaveRequest
            {
                EmployeeId = _employeeId, Type = LeaveType.Special, LeaveTypeId = typeId,
                StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 2), IsDraft = false
            };
            Result<long> secondSave = _leave.Save(second);
            Assert.That(secondSave.IsFailure, Is.True, "a second live request of a once-per-career type is refused");
            Assert.That(secondSave.ErrorCode, Is.EqualTo("Leave_OncePerCareer"));

            // A draft of the same type is still allowed (not yet a committed career use).
            var draft = new LeaveRequest
            {
                EmployeeId = _employeeId, Type = LeaveType.Special, LeaveTypeId = typeId,
                StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 2), IsDraft = true
            };
            Assert.That(_leave.Save(draft).IsSuccess, Is.True, "a draft does not trip the once-per-career guard");
        }

        // ── #39 : the loan lifecycle is fully audited (was: only SetStatus) ──
        [Test]
        public void LoanLifecycle_IsAudited()
        {
            var audit = new AuditService(_uowf, new NullLog()) { ActorProvider = () => "nadia" };
            var loans = new LoanService(_uowf) { Audit = audit };

            long id = loans.Save(new Loan
            {
                EmployeeId = _employeeId, Type = LoanType.Loan, Principal = 40000m, MonthlyInstallment = 10000m,
                StartYear = 2026, StartMonth = 1, Status = LoanStatus.Active
            }).Value;
            Assert.That(audit.GetForEntity("Loan", id).Any(e => e.Action == AuditAction.Created), Is.True, "loan creation is audited");
            Assert.That(audit.GetForEntity("Loan", id).First(e => e.Action == AuditAction.Created).Actor, Is.EqualTo("nadia"));

            Assert.That(loans.AddManualRepayment(id, 2026, 2, 5000m).IsSuccess, Is.True);
            Assert.That(audit.GetForEntity("Loan", id).Any(e => e.Action == AuditAction.Updated), Is.True, "a manual repayment is audited");

            Assert.That(loans.Delete(id).IsSuccess, Is.True);
            Assert.That(audit.GetForEntity("Loan", id).Any(e => e.Action == AuditAction.Deleted), Is.True, "loan deletion is audited");
        }

        // ── #49 : the certificate closing sentence never doubles the legal formula ──
        [Test]
        public void CertificateClosing_NeverDoublesTheLegalFormula()
        {
            const string formula = "pour servir et valoir ce que de droit";
            string standard = WorkCertificateDocument.ClosingFr(formula);
            Assert.That(System.Text.RegularExpressions.Regex.Matches(standard, System.Text.RegularExpressions.Regex.Escape(formula)).Count,
                Is.EqualTo(1), "the generic formula must appear exactly once, never doubled");

            Assert.That(WorkCertificateDocument.ClosingFr(""), Does.Contain(formula), "empty purpose falls back to the formula");

            string custom = WorkCertificateDocument.ClosingFr("pour un dossier bancaire");
            Assert.That(custom, Does.Contain("dossier bancaire"));
            Assert.That(custom, Does.Not.Contain(formula), "a real purpose replaces the formula, it is not appended");
        }

        private long InsertEmployee(string last, string first)
        {
            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                long id = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId, LastNameFr = last, FirstNameFr = first,
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, BaseSalary = 40000m, HireDate = new DateTime(2020, 1, 1), IsActive = true
                });
                uow.Commit();
                return id;
            }
        }

        private sealed class NullLog : ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }
}
