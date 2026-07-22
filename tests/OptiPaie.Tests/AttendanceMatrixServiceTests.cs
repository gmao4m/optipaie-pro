using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Attendance Matrix — the fast status-only entry that powers the new Excel-like
    /// grid. Tests run against a real SQLite file (so migration 0020's table rebuild and
    /// the new Mission status are exercised) and prove status-only entry needs no times,
    /// counts worked days correctly, feeds the payroll summary, and bulk-writes atomically.
    /// </summary>
    [TestFixture]
    public sealed class AttendanceMatrixServiceTests
    {
        private static readonly int Year = DateTime.Today.Year - 1;

        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private IAttendanceService _service;

        private long _companyId;
        private long _employeeId;
        private long _otherEmployeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-matrix-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new AttendanceService(_unitOfWorkFactory);

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(NewEmployee("BENALI", "Karim", "Production"));
                _otherEmployeeId = uow.Employees.Insert(NewEmployee("HADDAD", "Sofiane", "Finance"));
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* WAL still held */ }
        }

        private Employee NewEmployee(string last, string first, string department)
        {
            return new Employee
            {
                CompanyId = _companyId,
                LastNameFr = last,
                FirstNameFr = first,
                Department = department,
                Poste = "Opérateur",
                Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year - 3, 1, 1),
                BaseSalary = 50000m,
                IsActive = true
            };
        }

        // ---------------------------------------------------------------- department column

        [Test]
        public void Employee_Department_RoundTrips()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee e = uow.Employees.GetById(_employeeId);
                Assert.That(e.Department, Is.EqualTo("Production"), "the new Department column persists and reads back");
            }
        }

        // ---------------------------------------------------------------- status-only entry

        [Test]
        public void SetDayStatus_Present_NeedsNoTimesAndCountsAStandardDay()
        {
            var date = new DateTime(Year, 3, 2);

            Result result = _service.SetDayStatus(_employeeId, date, AttendanceStatus.Present);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            AttendanceRecord record = _service.Get(_employeeId, date);
            Assert.That(record, Is.Not.Null);
            Assert.That(record.Status, Is.EqualTo(AttendanceStatus.Present));
            Assert.That(record.WorkedHours, Is.EqualTo(8m), "a matrix Present counts as one standard day");
            Assert.That(record.CheckIn, Is.Null, "no times are required in the matrix");
        }

        [Test]
        public void SetDayStatus_Mission_IsWorkedAndPaid_NotAbsent()
        {
            _service.SetDayStatus(_employeeId, new DateTime(Year, 3, 3), AttendanceStatus.Mission);

            AttendanceRecord record = _service.Get(_employeeId, new DateTime(Year, 3, 3));
            Assert.That(record.Status, Is.EqualTo(AttendanceStatus.Mission), "the new Mission status persists (migration 0020)");
            Assert.That(record.WorkedHours, Is.EqualTo(8m));

            AttendanceSummary summary = _service.GetMonthlySummary(_employeeId, Year, 3);
            Assert.That(summary.PresentDays, Is.EqualTo(1), "mission counts as a present/worked day");
            Assert.That(summary.AbsentDays, Is.EqualTo(0), "mission is never an absence");
        }

        [Test]
        public void SetDayStatus_Absent_CarriesNoHoursAndFeedsTheAbsenceCount()
        {
            _service.SetDayStatus(_employeeId, new DateTime(Year, 3, 4), AttendanceStatus.Absent);

            AttendanceRecord record = _service.Get(_employeeId, new DateTime(Year, 3, 4));
            Assert.That(record.WorkedHours, Is.EqualTo(0m));
            Assert.That(_service.GetMonthlySummary(_employeeId, Year, 3).AbsentDays, Is.EqualTo(1));
        }

        [Test]
        public void SetDayStatus_OverwritesTheSameDay_NoDuplicate()
        {
            var date = new DateTime(Year, 3, 5);
            _service.SetDayStatus(_employeeId, date, AttendanceStatus.Present);
            _service.SetDayStatus(_employeeId, date, AttendanceStatus.Leave);

            Assert.That(_service.GetCompanyDay(_companyId, date).Count(r => r.EmployeeId == _employeeId), Is.EqualTo(1));
            Assert.That(_service.Get(_employeeId, date).Status, Is.EqualTo(AttendanceStatus.Leave));
        }

        [Test]
        public void SetDayStatus_FutureDate_IsSilentlySkipped()
        {
            Result result = _service.SetDayStatus(_employeeId, DateTime.Today.AddDays(3), AttendanceStatus.Present);

            Assert.That(result.IsSuccess, Is.True, "a matrix never records the future");
            Assert.That(_service.Get(_employeeId, DateTime.Today.AddDays(3)), Is.Null);
        }

        // ---------------------------------------------------------------- bulk

        [Test]
        public void SetDayStatusBulk_WritesEverythingAtomically()
        {
            var entries = new List<AttendanceDayStatus>();
            for (int day = 1; day <= 5; day++)
            {
                entries.Add(new AttendanceDayStatus(_employeeId, new DateTime(Year, 4, day), AttendanceStatus.Present));
                entries.Add(new AttendanceDayStatus(_otherEmployeeId, new DateTime(Year, 4, day), AttendanceStatus.Present));
            }

            Result result = _service.SetDayStatusBulk(entries);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(_service.GetCompanyMonth(_companyId, Year, 4).Count, Is.EqualTo(10), "5 days × 2 employees");
            Assert.That(_service.GetMonthlySummary(_employeeId, Year, 4).PresentDays, Is.EqualTo(5));
        }

        [Test]
        public void GetCompanyMonth_ReturnsOnlyThatMonth()
        {
            _service.SetDayStatus(_employeeId, new DateTime(Year, 6, 30), AttendanceStatus.Present);
            _service.SetDayStatus(_employeeId, new DateTime(Year, 7, 1), AttendanceStatus.Present);

            Assert.That(_service.GetCompanyMonth(_companyId, Year, 6).Count, Is.EqualTo(1));
            Assert.That(_service.GetCompanyMonth(_companyId, Year, 7).Count, Is.EqualTo(1));
        }

        [Test]
        public void SetDayStatus_AllStatuses_Persist_IncludingWeekendAndHoliday()
        {
            var day = new DateTime(Year, 5, 1);
            foreach (AttendanceStatus status in Enum.GetValues(typeof(AttendanceStatus)).Cast<AttendanceStatus>())
            {
                Result r = _service.SetDayStatus(_employeeId, day, status);
                Assert.That(r.IsSuccess, Is.True, status + ": " + r.Error);
                Assert.That(_service.Get(_employeeId, day).Status, Is.EqualTo(status));
            }
        }
    }
}
