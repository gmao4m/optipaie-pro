using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 4 — the proration basis drives the deduction. The default basis (MonthDays) must
    /// reproduce today EXACTLY (the real data is all June, so this uses a synthetic 31-day month to
    /// catch a /30-vs-/31 regression); the opt-in bases and half-days give the expected different,
    /// hand-computed results. Only unjustified absence (and half of a half-day) reduces pay.
    /// </summary>
    [TestFixture]
    public sealed class ProrationBasisRegressionTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private AttendanceService _attendance;
        private PayrollService _payroll;
        private WorkCalendarService _cal;
        private long _companyId, _emp;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-prorata-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            _employees = new EmployeeService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            _attendance = new AttendanceService(_uow, AccessContext.Unrestricted);
            var config = new ConfigurationService(_uow);
            _payroll = new PayrollService(_uow, config, new PayrollCalculationEngine(), AccessContext.Unrestricted);
            _cal = new WorkCalendarService(_uow, new HolidayService(_uow, AccessContext.Unrestricted), AccessContext.Unrestricted);

            _companyId = _companies.Create(new Company { NameFr = "SARL 31", Nif = "000000000000000" }).Value;
            _emp = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "JUILLET", FirstNameFr = "Test", Poste = "Agent",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2024, 1, 1), BaseSalary = 31000m, IsActive = true
            }).Value;
            // 3 unjustified absences in July 2026 (a 31-day month), all on work days (Mon/Tue/Wed).
            _attendance.SetDayStatusBulk(new List<AttendanceDayStatus>
            {
                new AttendanceDayStatus(_emp, new DateTime(2026, 7, 6), AttendanceStatus.Absent),
                new AttendanceDayStatus(_emp, new DateTime(2026, 7, 7), AttendanceStatus.Absent),
                new AttendanceDayStatus(_emp, new DateTime(2026, 7, 8), AttendanceStatus.Absent),
            });
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private BatchPayrollService Batch(bool withCalendar)
        {
            return new BatchPayrollService(
                _employees, new PayrollElementService(_uow, new PayrollElementValidator(), AccessContext.Unrestricted),
                new LoanService(_uow, AccessContext.Unrestricted), _attendance, _payroll,
                new ContractService(_uow, AccessContext.Unrestricted), new ArchiveService(_uow), _ => true,
                AccessContext.Unrestricted, withCalendar ? _cal : null);
        }

        private decimal Gross(BatchPayrollService batch)
        {
            PayrollGenerationRequest req = batch.BuildRequest(_companyId, _emp, 2026, 7);
            return _payroll.Preview(req).Totals.SalaireBrut;
        }

        private void SetBasis(ProrationBasis basis)
        {
            CompanyWorkCalendar c = _cal.GetSettings(_companyId);
            c.ProrationBasis = basis;
            _cal.SaveSettings(c);
        }

        [Test]
        public void Default_Historical_Fallback_Divides_By_31()
        {
            // No calendar wired = the historical path. 31000 × 28/31 = 28000 (/31, not /30 = 27900).
            PayrollGenerationRequest req = Batch(false).BuildRequest(_companyId, _emp, 2026, 7);
            Assert.That(req.WorkableDays, Is.EqualTo(31m));
            Assert.That(req.WorkedDays, Is.EqualTo(28m));
            Assert.That(Gross(Batch(false)), Is.EqualTo(28000m));
        }

        [Test]
        public void Default_Basis_Wired_Is_Identical_To_Historical()
        {
            // Calendar wired, default basis (MonthDays) → byte-identical to the historical path.
            Assert.That(Gross(Batch(true)), Is.EqualTo(28000m), "default basis must not move the number");
        }

        [Test]
        public void FixedThirtieths_Divides_By_30()
        {
            SetBasis(ProrationBasis.FixedThirtieths);
            // 31000 × 27/30 = 27900.
            Assert.That(Gross(Batch(true)), Is.EqualTo(27900m));
        }

        [Test]
        public void WorkingDays_Divides_By_The_Workable_Days()
        {
            SetBasis(ProrationBasis.WorkingDays);
            // July 2026 workable = 21 (22 Sun–Thu days − 5 Jul civil holiday). worked = 21 − 3 = 18.
            // 31000 × 18/21 = 26571.43.
            Assert.That(Gross(Batch(true)), Is.EqualTo(26571.43m));
        }

        [Test]
        public void HalfDay_Deducts_Exactly_Half()
        {
            // Add one half day → deductible = 3 + 0.5 = 3.5. worked = 31 − 3.5 = 27.5.
            _attendance.SetDayStatusBulk(new List<AttendanceDayStatus>
            {
                new AttendanceDayStatus(_emp, new DateTime(2026, 7, 9), AttendanceStatus.HalfDay),
            });
            // 31000 × 27.5/31 = 27500 (NOT 27000 for 4 days, NOT 28000 for 3).
            Assert.That(Gross(Batch(true)), Is.EqualTo(27500m));
        }

        [Test]
        public void Review_Row_Shows_The_Truth_Before_The_Run()
        {
            PayrollReviewRow row = Batch(true).BuildReviewRow(_companyId, _emp, 2026, 7);
            Assert.That(row.DaysDue, Is.EqualTo(31m));
            Assert.That(row.Worked, Is.EqualTo(28m));
            Assert.That(row.Absent, Is.EqualTo(3m));
            Assert.That(row.Half, Is.EqualTo(0m));
            Assert.That(row.Deduction, Is.EqualTo(3000m)); // 31000 × 3/31
            Assert.That(row.Basis, Is.EqualTo(ProrationBasis.MonthDays));
            Assert.That(row.NoAttendance, Is.False);
        }

        [Test]
        public void Review_Flags_No_Attendance_Plainly_Not_A_Silent_Full_Month()
        {
            long noData = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "SANS", FirstNameFr = "Pointage", Poste = "Agent",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2024, 1, 1), BaseSalary = 40000m, IsActive = true
            }).Value;

            PayrollReviewRow row = Batch(true).BuildReviewRow(_companyId, noData, 2026, 7);
            Assert.That(row.NoAttendance, Is.True, "missing attendance must be flagged, not silent");
            Assert.That(row.Deduction, Is.EqualTo(0m));
            Assert.That(row.Worked, Is.EqualTo(31m), "full month when no data");
        }
    }
}
