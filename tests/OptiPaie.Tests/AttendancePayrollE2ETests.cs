using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Payroll;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// End-to-end proof of the attendance → payroll link, the way a customer drives it: mark the
    /// month on the attendance service (the screen's own path), build the pre-run review, run the
    /// payroll, and read the archived payslip. Proves, to the centime:
    ///   • only unjustified absence (and half of a half day) reduces pay;
    ///   • approved leave and justified absence do NOT reduce pay;
    ///   • a company that records no attendance runs straight through (no review), yet a company
    ///     that DOES use attendance flags the employee whose data is missing;
    ///   • the same employee generated twice gives the identical payslip.
    /// The three proration bases and the half-day arithmetic are proven in
    /// <see cref="ProrationBasisRegressionTests"/>; this fixture proves the end-to-end chain and the
    /// review gate on top of them. The engine is never touched.
    /// </summary>
    [TestFixture]
    public sealed class AttendancePayrollE2ETests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private ContractService _contracts;
        private AttendanceService _attendance;
        private PayrollService _payroll;
        private ArchiveService _archive;
        private WorkCalendarService _cal;
        private BatchPayrollService _batch;

        private long _companyId;
        private static readonly int Year = DateTime.Today.Year;
        private const int Month = 6; // June: 30 days, no civil holiday in the default basis

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-att-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            _employees = new EmployeeService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            _contracts = new ContractService(_uow, AccessContext.Unrestricted);
            _attendance = new AttendanceService(_uow, AccessContext.Unrestricted);
            _archive = new ArchiveService(_uow);
            _payroll = new PayrollService(_uow, new ConfigurationService(_uow), new PayrollCalculationEngine(), AccessContext.Unrestricted);
            _cal = new WorkCalendarService(_uow, new HolidayService(_uow, AccessContext.Unrestricted), AccessContext.Unrestricted);
            _batch = new BatchPayrollService(
                _employees, new PayrollElementService(_uow, new PayrollElementValidator(), AccessContext.Unrestricted),
                new LoanService(_uow, AccessContext.Unrestricted), _attendance, _payroll,
                _contracts, _archive, _ => true, AccessContext.Unrestricted, _cal);

            _companyId = _companies.Create(new Company { NameFr = "SARL E2E", Nif = "000000000000000" }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private long AddEmployee(string last, decimal salary)
        {
            long id = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = last, FirstNameFr = "Test", Poste = "Agent",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(Year - 2, 1, 1), BaseSalary = salary, IsActive = true
            }).Value;
            long contractId = _contracts.CreateDraftFromEmployee(id).Value;
            _contracts.Activate(contractId);
            return id;
        }

        private decimal Net(long employeeId) =>
            _payroll.Preview(_batch.BuildRequest(_companyId, employeeId, Year, Month)).Totals.NetSalaire;

        // ---------------------------------------------------------------- leave / justified absence

        [Test]
        public void LeaveAndJustifiedAbsence_DoNotReducePay()
        {
            long onLeave = AddEmployee("CONGE", 50000m);
            long baseline = AddEmployee("PLEIN", 50000m); // no attendance at all → full month

            // A week of approved leave and two justified absences — none of which deducts.
            _attendance.SetDayStatusBulk(new List<AttendanceDayStatus>
            {
                new AttendanceDayStatus(onLeave, new DateTime(Year, Month, 2), AttendanceStatus.Leave),
                new AttendanceDayStatus(onLeave, new DateTime(Year, Month, 3), AttendanceStatus.Leave),
                new AttendanceDayStatus(onLeave, new DateTime(Year, Month, 4), AttendanceStatus.Leave),
                new AttendanceDayStatus(onLeave, new DateTime(Year, Month, 5), AttendanceStatus.JustifiedAbsence),
                new AttendanceDayStatus(onLeave, new DateTime(Year, Month, 6), AttendanceStatus.JustifiedAbsence),
            });

            PayrollReviewRow row = _batch.BuildReviewRow(_companyId, onLeave, Year, Month);
            Assert.That(row.Absent, Is.EqualTo(0m), "justified absence is not unjustified");
            Assert.That(row.Half, Is.EqualTo(0m));
            Assert.That(row.Deduction, Is.EqualTo(0m), "neither leave nor justified absence reduces pay");
            Assert.That(row.Worked, Is.EqualTo(DateTime.DaysInMonth(Year, Month)), "paid the full month");
            Assert.That(row.NoAttendance, Is.False, "the month IS recorded (leave days), just not deductible");

            Assert.That(Net(onLeave), Is.EqualTo(Net(baseline)),
                "a month of leave pays exactly the same as a full worked month");
        }

        [Test]
        public void UnjustifiedAbsence_ReducesPay_AndTheArchivedPayslipMatches()
        {
            long id = AddEmployee("ABSENT", 60000m);
            _attendance.SetDayStatusBulk(new List<AttendanceDayStatus>
            {
                new AttendanceDayStatus(id, new DateTime(Year, Month, 3), AttendanceStatus.Absent),
                new AttendanceDayStatus(id, new DateTime(Year, Month, 4), AttendanceStatus.Absent),
            });

            PayrollReviewRow row = _batch.BuildReviewRow(_companyId, id, Year, Month);
            Assert.That(row.Absent, Is.EqualTo(2m));
            Assert.That(row.Deduction, Is.GreaterThan(0m));
            Assert.That(PayrollReviewPolicy.NeedsReview(new[] { row }), Is.True, "a real deduction must be reviewed");

            // Run the batch and read the archived payslip — its net equals a fresh engine calculation.
            BatchPayrollResult result = _batch.Run(_companyId, Year, Month);
            Assert.That(result.Succeeded, Is.EqualTo(1));
            Payslip archived = _archive.GetPayslip(result.Results.Single().PayslipId);
            Assert.That(archived.NetSalaire, Is.EqualTo(Net(id)), "the archived net reflects the absence deduction");
            Assert.That(archived.NetSalaire, Is.LessThan(60000m), "two unjustified absences reduce the net");
        }

        // ---------------------------------------------------------------- the review gate

        [Test]
        public void CleanRecordedMonth_RunsStraightThrough()
        {
            long id = AddEmployee("PRESENT", 50000m);
            // Every work day present — attendance IS on file, nothing deducts.
            var days = new List<AttendanceDayStatus>();
            for (int d = 1; d <= 5; d++) days.Add(new AttendanceDayStatus(id, new DateTime(Year, Month, d + 1), AttendanceStatus.Present));
            _attendance.SetDayStatusBulk(days);

            PayrollReviewRow row = _batch.BuildReviewRow(_companyId, id, Year, Month);
            Assert.That(row.NoAttendance, Is.False);
            Assert.That(row.Deduction, Is.EqualTo(0m));
            Assert.That(PayrollReviewPolicy.NeedsReview(new[] { row }), Is.False,
                "a clean recorded month is not stopped for a table of zeros");
        }

        [Test]
        public void CompanyThatRecordsNoAttendance_RunsStraightThrough()
        {
            // The 300-customer case: nobody ever opened the attendance screen.
            AddEmployee("UN", 50000m);
            AddEmployee("DEUX", 48000m);
            AddEmployee("TROIS", 55000m);

            IReadOnlyList<PayrollReviewRow> review = _batch.BuildReview(_companyId, Year, Month);
            Assert.That(review.All(r => r.NoAttendance), Is.True, "no attendance recorded anywhere");
            Assert.That(PayrollReviewPolicy.NeedsReview(review), Is.False,
                "a company that does not use attendance is never stopped — full month is its model, not an error");
        }

        [Test]
        public void AttendanceInUseButMissingForSomeone_IsReviewed()
        {
            long present = AddEmployee("POINTE", 50000m);
            AddEmployee("OUBLIE", 48000m); // no attendance → the one you forgot

            _attendance.SetDayStatus(present, new DateTime(Year, Month, 2), AttendanceStatus.Present);

            IReadOnlyList<PayrollReviewRow> review = _batch.BuildReview(_companyId, Year, Month);
            Assert.That(review.Any(r => r.NoAttendance), Is.True);
            Assert.That(review.Any(r => !r.NoAttendance), Is.True);
            Assert.That(PayrollReviewPolicy.NeedsReview(review), Is.True,
                "attendance is in use, so the employee with none is surfaced, not silently paid full");
        }

        // ---------------------------------------------------------------- reproducibility

        [Test]
        public void SameEmployeeGeneratedTwice_IsReproducibleToTheCentime()
        {
            long id = AddEmployee("STABLE", 57300m);
            _attendance.SetDayStatus(id, new DateTime(Year, Month, 3), AttendanceStatus.Absent);
            _attendance.SetDayStatus(id, new DateTime(Year, Month, 4), AttendanceStatus.HalfDay);

            PayrollTotals first = _payroll.Preview(_batch.BuildRequest(_companyId, id, Year, Month)).Totals;
            PayrollTotals second = _payroll.Preview(_batch.BuildRequest(_companyId, id, Year, Month)).Totals;

            Assert.That(second.SalaireBrut, Is.EqualTo(first.SalaireBrut));
            Assert.That(second.NetSalaire, Is.EqualTo(first.NetSalaire));
            Assert.That(second.Irg, Is.EqualTo(first.Irg));
            Assert.That(second.CnasEmployee, Is.EqualTo(first.CnasEmployee));
        }
    }
}
