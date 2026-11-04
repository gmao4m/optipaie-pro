using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The pointage Phase-2 technical fixes: UPSERT bulk save (no duplicate day rows, in-place update),
    /// atomic ClearDayBulk (leave-protected), and the batch-payroll attendance cache producing the
    /// IDENTICAL worked-days a per-employee read produces (engine figures unchanged). Zero data loss.
    /// </summary>
    [TestFixture]
    public sealed class AttendanceReworkTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private AttendanceService _attendance;
        private long _companyId, _empId;
        private static readonly int Year = DateTime.Today.Year - 1;
        private const int Month = 6;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-att-rework-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _attendance = new AttendanceService(_uow);
            _companyId = _companies.Create(new Company { NameFr = "SARL Att", Nif = "000000000000000" }).Value;
            _empId = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "B", FirstNameFr = "K", Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash, ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year - 1, 1, 1), BaseSalary = 50000m, IsActive = true
            }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private int ActiveRecordCount()
        {
            using (IUnitOfWork uow = _uow.Create())
                return uow.Attendance.GetByEmployeeRange(_empId, new DateTime(Year, Month, 1), new DateTime(Year, Month, 28)).Count();
        }

        [Test]
        public void Upsert_NoDuplicateRows_UpdatesInPlace()
        {
            var day = new DateTime(Year, Month, 3);
            _attendance.SetDayStatusBulk(new[] { new AttendanceDayStatus(_empId, day, AttendanceStatus.Present) });
            _attendance.SetDayStatusBulk(new[] { new AttendanceDayStatus(_empId, day, AttendanceStatus.Absent) });

            Assert.That(ActiveRecordCount(), Is.EqualTo(1), "re-saving the same day must not create a 2nd active row");
            AttendanceRecord rec = _attendance.Get(_empId, day);
            Assert.That(rec.Status, Is.EqualTo(AttendanceStatus.Absent), "the day reflects the latest status (update in place)");
        }

        [Test]
        public void ClearDayBulk_RemovesAllGivenDays_InOneCall()
        {
            var days = Enumerable.Range(1, 10).Select(d => new DateTime(Year, Month, d))
                .Where(d => d.DayOfWeek != DayOfWeek.Friday && d.DayOfWeek != DayOfWeek.Saturday).ToList();
            _attendance.SetDayStatusBulk(days.Select(d => new AttendanceDayStatus(_empId, d, AttendanceStatus.Present)).ToList());
            Assert.That(ActiveRecordCount(), Is.EqualTo(days.Count));

            Result result = _attendance.ClearDayBulk(days.Select(d => new AttendanceDayStatus(_empId, d, AttendanceStatus.Present)).ToList());
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(ActiveRecordCount(), Is.EqualTo(0), "every targeted day erased in one atomic call");
        }

        [Test]
        public void ClearDayBulk_ProtectsLeaveSyncedDays()
        {
            var normal = new DateTime(Year, Month, 2);
            var leaveDay = new DateTime(Year, Month, 3);
            _attendance.SetDayStatusBulk(new[] { new AttendanceDayStatus(_empId, normal, AttendanceStatus.Present) });
            // A leave-synced record is tagged "[Congé]" in Notes (as the Leave module writes it).
            _attendance.Save(new AttendanceRecord { EmployeeId = _empId, WorkDate = leaveDay, Status = AttendanceStatus.Leave, Notes = "[Congé] approuvé" });

            _attendance.ClearDayBulk(new[]
            {
                new AttendanceDayStatus(_empId, normal, AttendanceStatus.Present),
                new AttendanceDayStatus(_empId, leaveDay, AttendanceStatus.Leave),
            });

            Assert.That(_attendance.Get(_empId, normal), Is.Null, "the normal day was erased");
            AttendanceRecord leave = _attendance.Get(_empId, leaveDay);
            Assert.That(leave, Is.Not.Null, "a leave-synced day is never erased here");
            Assert.That(leave.Status, Is.EqualTo(AttendanceStatus.Leave));
        }

        [Test]
        public void BatchAttendanceCache_ProducesIdenticalWorkedDays_AsPerEmployeeRead()
        {
            // Some absences this month.
            var absents = new[] { 2, 3, 4 }.Select(d => new DateTime(Year, Month, d)).ToList();
            _attendance.SetDayStatusBulk(absents.Select(d => new AttendanceDayStatus(_empId, d, AttendanceStatus.Absent)).ToList());

            var elements = new PayrollElementService(_uow, new PayrollElementValidator());
            var batch = new BatchPayrollService(_employees, elements, new LoanService(_uow),
                _attendance, new PayrollService(_uow, new ConfigurationService(_uow), new PayrollCalculationEngine()),
                new ContractService(_uow), new ArchiveService(_uow), _ => true);

            PayrollGenerationRequest single = batch.BuildRequest(_companyId, _empId, Year, Month, null);
            var cache = _attendance.GetCompanyMonthlySummary(_companyId, Year, Month)
                .GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.First());
            PayrollGenerationRequest cached = batch.BuildRequest(_companyId, _empId, Year, Month, cache);

            Assert.That(cached.WorkedDays, Is.EqualTo(single.WorkedDays), "cache path yields identical worked days");
            Assert.That(cached.WorkableDays, Is.EqualTo(single.WorkableDays));
            Assert.That(cached.WorkedHours, Is.EqualTo(single.WorkedHours));
            Assert.That(single.WorkedDays, Is.LessThan(DateTime.DaysInMonth(Year, Month)), "absences actually reduced worked days (meaningful check)");
        }
    }
}
