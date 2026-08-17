using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Mandatory proofs for the Congés module — the state machine, the reserved/available
    /// balance, overlap, ATOMICITY of an approval, payroll non-regression, and a full
    /// create→submit→approve smoke — all on a real SQLite file through the real services.
    /// A refactor without these proofs is not a delivery.
    /// </summary>
    [TestFixture]
    public sealed class LeaveWorkflowTests
    {
        private static readonly int Year = DateTime.Today.Year - 1;

        private string _directory;
        private IUnitOfWorkFactory _uow;
        private LeaveService _leave;
        private IAttendanceService _attendance;
        private AuditService _audit;
        private IPayrollService _payroll;

        private long _companyId;
        private long _employeeId;
        private DateTime _weekStart; // a Sunday — start of the Algerian working week

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-leavewf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _audit = new AuditService(_uow, new SilentLogger());
            _leave = new LeaveService(_uow) { Audit = _audit };
            _attendance = new AttendanceService(_uow);
            _payroll = new PayrollService(_uow, new ConfigurationService(_uow), new PayrollCalculationEngine());

            using (IUnitOfWork uow = _uow.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Preuve", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(NewEmployee());
                uow.Commit();
            }

            _weekStart = FirstDayOfWeek(Year, 6, DayOfWeek.Sunday);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* WAL still held */ }
        }

        private Employee NewEmployee()
        {
            return new Employee
            {
                CompanyId = _companyId,
                LastNameFr = "BENALI",
                FirstNameFr = "Karim",
                Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year - 4, 1, 1),
                BaseSalary = 40000m,
                IsActive = true
            };
        }

        private LeaveRequest Req(LeaveType type, int startOffset, int endOffset, bool draft = false)
        {
            return new LeaveRequest
            {
                EmployeeId = _employeeId,
                Type = type,
                StartDate = _weekStart.AddDays(startOffset),
                EndDate = _weekStart.AddDays(endOffset),
                IsDraft = draft,
                Reason = "Test"
            };
        }

        private static DateTime FirstDayOfWeek(int year, int month, DayOfWeek dayOfWeek)
        {
            var day = new DateTime(year, month, 1);
            while (day.DayOfWeek != dayOfWeek) day = day.AddDays(1);
            return day;
        }

        // ============================================================ PREUVE 1 — state machine

        [Test]
        public void StateMachine_AllowedTransitions_AllPass()
        {
            // Brouillon → En attente
            long id = _leave.Save(Req(LeaveType.Annual, 0, 2, draft: true)).Value;
            Assert.That(_leave.Get(id).IsDraft, Is.True, "saved as a draft");

            Assert.That(_leave.Submit(id).IsSuccess, Is.True, "Brouillon → En attente");
            LeaveRequest submitted = _leave.Get(id);
            Assert.That(submitted.IsDraft, Is.False);
            Assert.That(submitted.Status, Is.EqualTo(LeaveStatus.Pending));

            // En attente → Brouillon (return) and back
            Assert.That(_leave.ReturnToDraft(id, "à corriger").IsSuccess, Is.True, "En attente → Brouillon");
            Assert.That(_leave.Get(id).IsDraft, Is.True);
            Assert.That(_leave.Submit(id).IsSuccess, Is.True, "re-submit");

            // En attente → Approuvée
            Assert.That(_leave.Approve(id, "accordé").IsSuccess, Is.True, "En attente → Approuvée");
            Assert.That(_leave.Get(id).Status, Is.EqualTo(LeaveStatus.Approved));

            // Approuvée → Annulée
            Assert.That(_leave.Cancel(id, "reporté").IsSuccess, Is.True, "Approuvée → Annulée");
            Assert.That(_leave.Get(id).Status, Is.EqualTo(LeaveStatus.Cancelled));

            // En attente → Refusée (a second request, with a motive)
            long id2 = _leave.Save(Req(LeaveType.Annual, 7, 9)).Value; // live (submitted)
            Assert.That(_leave.Reject(id2, "effectif insuffisant").IsSuccess, Is.True, "En attente → Refusée");
            Assert.That(_leave.Get(id2).Status, Is.EqualTo(LeaveStatus.Rejected));
        }

        [Test]
        public void StateMachine_ForbiddenTransitions_AllRejected()
        {
            // Approve a DRAFT (must be submitted first)
            long draft = _leave.Save(Req(LeaveType.Annual, 0, 2, draft: true)).Value;
            Assert.That(_leave.Approve(draft, null).IsFailure, Is.True, "a draft cannot be approved");
            Assert.That(_leave.Reject(draft, "x").IsFailure, Is.True, "a draft cannot be refused");
            Assert.That(_leave.Cancel(draft, "x").IsFailure, Is.True, "a draft cannot be cancelled");
            Assert.That(_leave.ReturnToDraft(draft, "x").IsFailure, Is.True, "a draft cannot be returned to draft");

            // Reject → then Approve is impossible
            long rejected = _leave.Save(Req(LeaveType.Annual, 7, 9)).Value;
            _leave.Reject(rejected, "non");
            Assert.That(_leave.Approve(rejected, null).IsFailure, Is.True, "a refused request cannot be approved");
            Assert.That(_leave.Cancel(rejected, "x").IsFailure, Is.True, "a refused request cannot be cancelled");

            // Approve → then Reject is impossible; Cancel a PENDING is impossible
            long pending = _leave.Save(Req(LeaveType.Annual, 14, 16)).Value;
            Assert.That(_leave.Cancel(pending, "x").IsFailure, Is.True, "only an approved leave can be cancelled");
            _leave.Approve(pending, null);
            Assert.That(_leave.Reject(pending, "x").IsFailure, Is.True, "an approved leave cannot be refused");

            // Motive mandatory for refusal and cancellation
            long m = _leave.Save(Req(LeaveType.Annual, 21, 23)).Value;
            Assert.That(_leave.Reject(m, "  ").IsFailure, Is.True, "refusal needs a motive");
            _leave.Reject(m, "motif");
            long m2 = _leave.Save(Req(LeaveType.Annual, 28, 30)).Value;
            _leave.Approve(m2, null);
            Assert.That(_leave.Cancel(m2, null).IsFailure, Is.True, "cancellation needs a motive");
        }

        // ============================================================ PREUVE 2 — balance reserve/release

        [Test]
        public void Balance_SubmittedRequestReserves_RefusalReleases()
        {
            LeaveBalance before = _leave.GetBalance(_employeeId, Year);
            Assert.That(before.Entitlement, Is.EqualTo(30m));
            Assert.That(before.Available, Is.EqualTo(30m), "acquis − consommé − réservé");

            long id = _leave.Save(Req(LeaveType.Annual, 0, 4)).Value; // 5 working days, submitted
            LeaveBalance reserved = _leave.GetBalance(_employeeId, Year);
            Assert.That(reserved.Pending, Is.EqualTo(5m), "réservé");
            Assert.That(reserved.Taken, Is.EqualTo(0m), "not yet consumed");
            Assert.That(reserved.Available, Is.EqualTo(25m), "reservation lowers the available balance");

            _leave.Reject(id, "non");
            LeaveBalance released = _leave.GetBalance(_employeeId, Year);
            Assert.That(released.Pending, Is.EqualTo(0m), "refusal releases the reservation");
            Assert.That(released.Available, Is.EqualTo(30m));
        }

        [Test]
        public void Balance_ApprovalConsumes_CancellationReleases()
        {
            long id = _leave.Save(Req(LeaveType.Annual, 0, 4)).Value; // 5 days
            _leave.Approve(id, null);

            LeaveBalance consumed = _leave.GetBalance(_employeeId, Year);
            Assert.That(consumed.Taken, Is.EqualTo(5m), "consommé");
            Assert.That(consumed.Pending, Is.EqualTo(0m));
            Assert.That(consumed.Available, Is.EqualTo(25m));

            _leave.Cancel(id, "annulé");
            LeaveBalance released = _leave.GetBalance(_employeeId, Year);
            Assert.That(released.Taken, Is.EqualTo(0m), "cancellation releases the consumption");
            Assert.That(released.Available, Is.EqualTo(30m));
        }

        [Test]
        public void Balance_ADraftReservesNothing()
        {
            _leave.Save(Req(LeaveType.Annual, 0, 4, draft: true)); // 5 days, but a DRAFT
            LeaveBalance balance = _leave.GetBalance(_employeeId, Year);
            Assert.That(balance.Pending, Is.EqualTo(0m), "a draft reserves nothing");
            Assert.That(balance.Available, Is.EqualTo(30m));
        }

        [Test]
        public void Balance_InsufficientForConsumingType_IsRejected()
        {
            // Cap the entitlement low so the request cannot fit.
            _leave.SaveSettings(new LeaveSettings { DaysPerMonth = 0.25m, AnnualCap = 3m, ExcludeRestDays = true });

            Result<long> tooMuch = _leave.Save(Req(LeaveType.Annual, 0, 4)); // 5 days > 3 available
            Assert.That(tooMuch.IsFailure, Is.True, "not enough annual balance");
            Assert.That(tooMuch.ErrorCode, Is.EqualTo("Leave_InsufficientBalance"));

            // A non-consuming type (sans solde) is never blocked by the annual balance.
            Assert.That(_leave.Save(Req(LeaveType.Unpaid, 0, 4)).IsSuccess, Is.True, "unpaid never touches the annual balance");
        }

        // ============================================================ PREUVE 3 — overlap

        [Test]
        public void Overlap_WithLiveRequest_IsRejected()
        {
            _leave.Save(Req(LeaveType.Annual, 0, 3)); // live
            Result<long> clash = _leave.Save(Req(LeaveType.Sick, 2, 4)); // overlaps
            Assert.That(clash.IsFailure, Is.True);
            Assert.That(clash.ErrorCode, Is.EqualTo("Leave_Overlap"));
        }

        [Test]
        public void Overlap_ADraftIsNotLive_ButSubmittingItIsRejected()
        {
            _leave.Save(Req(LeaveType.Annual, 0, 3)); // live A
            long draft = _leave.Save(Req(LeaveType.Annual, 1, 2, draft: true)).Value; // draft B overlaps A — allowed
            Assert.That(draft, Is.GreaterThan(0), "a draft may overlap a live request (it reserves nothing)");

            Result submit = _leave.Submit(draft);
            Assert.That(submit.IsFailure, Is.True, "submitting the draft makes it live → overlap is enforced");
            Assert.That(_leave.Get(draft).IsDraft, Is.True, "the failed submit leaves it a draft (no partial state)");
        }

        // ============================================================ PREUVE 4 — atomicity

        [Test]
        public void Approve_FailingMidway_LeavesNoPartialRow()
        {
            // A submitted 3-day request (so approval writes 3 attendance rows).
            long id = _leave.Save(Req(LeaveType.Annual, 0, 2)).Value;

            // A service whose 2nd attendance insert throws in the middle of the approval.
            var failingFactory = new FailingAttendanceFactory(_uow, failOnInsertNumber: 2);
            var failingService = new LeaveService(failingFactory) { Audit = _audit };

            Assert.Throws<InvalidOperationException>(() => failingService.Approve(id, "OK"),
                "the injected mid-approval failure propagates");

            // Nothing partial survives — not the status, not attendance, not the balance, not audit.
            Assert.That(_leave.Get(id).Status, Is.EqualTo(LeaveStatus.Pending), "leave status rolled back");
            for (int i = 0; i <= 2; i++)
            {
                Assert.That(_attendance.Get(_employeeId, _weekStart.AddDays(i)), Is.Null,
                    "no attendance row was left behind (day " + i + ")");
            }
            LeaveBalance balance = _leave.GetBalance(_employeeId, Year);
            Assert.That(balance.Taken, Is.EqualTo(0m), "no consumption before the write succeeded");
            Assert.That(balance.Pending, Is.EqualTo(3m), "still merely reserved, exactly as before the failed approval");

            IReadOnlyList<AuditEntry> trail = _audit.GetForEntity("Leave", id);
            Assert.That(trail.Any(e => e.Action == AuditAction.Approved), Is.False,
                "no approval was recorded — audit is written only after the transaction commits");
        }

        // ============================================================ PREUVE 5 — payroll non-regression

        [Test]
        public void Payroll_PaidLeaveKeepsTheAmount_UnpaidDeductsOnlyViaAttendance()
        {
            const int MonthDays = 26;
            int month = _weekStart.Month;

            decimal Brut(decimal workedDays)
            {
                PayrollResult r = _payroll.Preview(new PayrollGenerationRequest
                {
                    CompanyId = _companyId, EmployeeId = _employeeId, Year = Year, Month = month,
                    WorkedDays = workedDays, WorkableDays = MonthDays, BaseSalaryOverride = null
                });
                Assert.That(r.IsSuccess, Is.True, "payroll preview succeeds");
                return r.Totals.SalaireBrut;
            }

            decimal AbsentDays() => _attendance.GetMonthlySummary(_employeeId, Year, month).AbsentDays;

            // Baseline — no leave, full month worked.
            decimal baseline = Brut(MonthDays);
            Assert.That(baseline, Is.GreaterThan(0m));

            // PAID annual leave (3 days) → attendance status "Congé", NEVER absent → identical amount.
            long paid = _leave.Save(Req(LeaveType.Annual, 0, 2)).Value;
            _leave.Approve(paid, null);
            Assert.That(AbsentDays(), Is.EqualTo(0), "paid leave is not an absence");
            Assert.That(Brut(MonthDays - AbsentDays()), Is.EqualTo(baseline),
                "montant identique : le congé payé ne change pas la paie");

            // UNPAID leave (2 days) → attendance status "Absent" → the ONLY payroll effect.
            long unpaid = _leave.Save(Req(LeaveType.Unpaid, 7, 8)).Value;
            _leave.Approve(unpaid, null);
            Assert.That(AbsentDays(), Is.EqualTo(2), "unpaid leave reaches payroll only as 2 absent days");

            decimal viaLeave = Brut(MonthDays - AbsentDays());       // worked days derived from attendance
            decimal viaHand = Brut(MonthDays - 2);                   // the same 2 days entered by hand
            Assert.That(viaLeave, Is.EqualTo(viaHand),
                "the unpaid deduction equals exactly a manual 2-day absence — the engine is untouched");
            Assert.That(viaLeave, Is.LessThan(baseline), "and it does reduce the gross");
        }

        // ============================================================ PREUVE 6 — smoke E2E

        [Test]
        public void Smoke_CreateSubmitApprove_FlowsIntoAttendance()
        {
            long id = _leave.Save(Req(LeaveType.Annual, 0, 2, draft: true)).Value; // create as draft
            Assert.That(_leave.Submit(id).IsSuccess, Is.True);                       // submit
            Assert.That(_leave.Approve(id, "OK").IsSuccess, Is.True);                // approve

            for (int i = 0; i <= 2; i++)
            {
                AttendanceRecord day = _attendance.Get(_employeeId, _weekStart.AddDays(i));
                Assert.That(day, Is.Not.Null, "the approved day appears in attendance");
                Assert.That(day.Status, Is.EqualTo(AttendanceStatus.Leave));
            }

            Assert.That(_leave.GetBalance(_employeeId, Year).Taken, Is.EqualTo(3m), "the balance reflects the approval");
        }

        // ============================================================ test doubles

        private sealed class SilentLogger : ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }

        /// <summary>Factory that returns a UoW whose attendance repository fails on the Nth insert.</summary>
        private sealed class FailingAttendanceFactory : IUnitOfWorkFactory
        {
            private readonly IUnitOfWorkFactory _inner;
            private readonly int _failOnInsertNumber;

            public FailingAttendanceFactory(IUnitOfWorkFactory inner, int failOnInsertNumber)
            {
                _inner = inner;
                _failOnInsertNumber = failOnInsertNumber;
            }

            public IUnitOfWork Create() => new FailingAttendanceUnitOfWork(_inner.Create(), _failOnInsertNumber);
        }

        /// <summary>
        /// Delegates everything to a real UoW (sharing its connection and transaction) except the
        /// attendance repository, which is wrapped to throw partway through — so a rollback must
        /// undo the leave row AND the first attendance row together.
        /// </summary>
        private sealed class FailingAttendanceUnitOfWork : IUnitOfWork
        {
            private readonly IUnitOfWork _inner;
            private readonly IAttendanceRepository _attendance;

            public FailingAttendanceUnitOfWork(IUnitOfWork inner, int failOnInsertNumber)
            {
                _inner = inner;
                _attendance = new ThrowingAttendanceRepository(inner.Attendance, failOnInsertNumber);
            }

            public IAttendanceRepository Attendance => _attendance;

            public ICompanyRepository Companies => _inner.Companies;
            public IEmployeeRepository Employees => _inner.Employees;
            public IPayrollElementRepository PayrollElements => _inner.PayrollElements;
            public IEmployeeElementRepository EmployeeElements => _inner.EmployeeElements;
            public IPayrollRunRepository PayrollRuns => _inner.PayrollRuns;
            public IPayslipRepository Payslips => _inner.Payslips;
            public IPayrollDetailRepository PayrollDetails => _inner.PayrollDetails;
            public IArchiveDocumentRepository ArchiveDocuments => _inner.ArchiveDocuments;
            public ILegalParameterRepository LegalParameters => _inner.LegalParameters;
            public IAppSettingRepository AppSettings => _inner.AppSettings;
            public ILanguageRepository Languages => _inner.Languages;
            public IBackupRecordRepository BackupRecords => _inner.BackupRecords;
            public ILeaveRepository Leave => _inner.Leave;
            public ILeaveTypeRepository LeaveTypes => _inner.LeaveTypes;
            public IHolidayRepository Holidays => _inner.Holidays;
            public ILoanRepository Loans => _inner.Loans;
            public IContractRepository Contracts => _inner.Contracts;
            public IPerformanceRepository Performance => _inner.Performance;
            public IAssetRepository Assets => _inner.Assets;
            public IDepartmentRepository Departments => _inner.Departments;
            public ITrainingRepository Training => _inner.Training;
            public IAtsRepository Ats => _inner.Ats;
            public IWorkCertificateRepository Certificates => _inner.Certificates;
            public IAuditRepository Audit => _inner.Audit;
            public IUserRepository Users => _inner.Users;

            public void BeginTransaction() => _inner.BeginTransaction();
            public void Commit() => _inner.Commit();
            public void Rollback() => _inner.Rollback();
            public void Dispose() => _inner.Dispose();
        }

        private sealed class ThrowingAttendanceRepository : IAttendanceRepository
        {
            private readonly IAttendanceRepository _inner;
            private readonly int _failOn;
            private int _inserts;

            public ThrowingAttendanceRepository(IAttendanceRepository inner, int failOn)
            {
                _inner = inner;
                _failOn = failOn;
            }

            public long Insert(AttendanceRecord record)
            {
                _inserts++;
                if (_inserts >= _failOn)
                {
                    throw new InvalidOperationException("Injected attendance failure at insert #" + _inserts);
                }

                return _inner.Insert(record);
            }

            public AttendanceRecord GetById(long id) => _inner.GetById(id);
            public AttendanceRecord GetByEmployeeAndDate(long employeeId, DateTime workDate) => _inner.GetByEmployeeAndDate(employeeId, workDate);
            public IEnumerable<AttendanceRecord> GetByEmployeeRange(long employeeId, DateTime from, DateTime to) => _inner.GetByEmployeeRange(employeeId, from, to);
            public IEnumerable<AttendanceRecord> GetByCompanyRange(long companyId, DateTime from, DateTime to) => _inner.GetByCompanyRange(companyId, from, to);
            public void Update(AttendanceRecord record) => _inner.Update(record);
            public void SoftDelete(long id) => _inner.SoftDelete(id);
        }
    }
}
