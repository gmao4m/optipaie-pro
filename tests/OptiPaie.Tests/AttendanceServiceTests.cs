using System;
using System.Collections.Generic;
using System.IO;
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
    /// Attendance module — integration tests against a real SQLite file (real
    /// migrations, real repositories, real service). They prove the module shares the
    /// employee/company tables rather than duplicating them, that a day can never be
    /// recorded twice, and that the hours/lateness/overtime payroll consumes are
    /// derived consistently.
    /// </summary>
    [TestFixture]
    public sealed class AttendanceServiceTests
    {
        /// <summary>Last year — every test date must be in the past (future pointage is rejected).</summary>
        private static readonly int Year = DateTime.Today.Year - 1;

        private string _directory;
        private SqliteConnectionFactory _factory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private IAttendanceService _service;

        private long _companyId;
        private long _employeeId;
        private long _otherEmployeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-attendance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = _factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(_factory);
            _service = new AttendanceService(_unitOfWorkFactory);

            SeedSharedData();
            _service.SaveSettings(new AttendanceSettings
            {
                StandardStart = "08:00",
                StandardHours = 8m,
                LateToleranceMinutes = 10
            });
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        /// <summary>Creates the company and employees through the SHARED repositories.</summary>
        private void SeedSharedData()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company
                {
                    NameFr = "SARL Test",
                    Nif = "000000000000000"
                });

                _employeeId = uow.Employees.Insert(NewEmployee(_companyId, "BENALI", "Karim"));
                _otherEmployeeId = uow.Employees.Insert(NewEmployee(_companyId, "HADDAD", "Sofiane"));

                uow.Commit();
            }
        }

        private static Employee NewEmployee(long companyId, string last, string first)
        {
            return new Employee
            {
                CompanyId = companyId,
                LastNameFr = last,
                FirstNameFr = first,
                Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash,
                HireDate = new DateTime(Year - 4, 1, 1),
                BaseSalary = 50000m,
                ContractType = ContractType.Cdi,
                IsActive = true
            };
        }

        // ---------------------------------------------------------------- calculations

        [Test]
        public void Save_ComputesWorkedHoursAndOvertime()
        {
            Result<long> saved = _service.Save(new AttendanceRecord
            {
                EmployeeId = _employeeId,
                WorkDate = new DateTime(Year, 3, 2),
                Status = AttendanceStatus.Present,
                CheckIn = "08:00",
                CheckOut = "18:30"
            });

            Assert.That(saved.IsSuccess, Is.True, saved.Error);

            AttendanceRecord record = _service.Get(_employeeId, new DateTime(Year, 3, 2));
            Assert.That(record, Is.Not.Null);
            Assert.That(record.WorkedHours, Is.EqualTo(10.5m));
            Assert.That(record.OvertimeHours, Is.EqualTo(2.5m), "10.5 h worked against an 8 h standard day");
            Assert.That(record.LateMinutes, Is.EqualTo(0));
            Assert.That(record.Status, Is.EqualTo(AttendanceStatus.Present));
        }

        [Test]
        public void Save_ArrivalWithinTolerance_IsNotLate()
        {
            _service.Save(Day(_employeeId, new DateTime(Year, 3, 3), "08:09", "16:00"));

            AttendanceRecord record = _service.Get(_employeeId, new DateTime(Year, 3, 3));
            Assert.That(record.LateMinutes, Is.EqualTo(0));
            Assert.That(record.Status, Is.EqualTo(AttendanceStatus.Present));
        }

        [Test]
        public void Save_ArrivalBeyondTolerance_IsLate()
        {
            _service.Save(Day(_employeeId, new DateTime(Year, 3, 4), "08:35", "16:00"));

            AttendanceRecord record = _service.Get(_employeeId, new DateTime(Year, 3, 4));
            Assert.That(record.LateMinutes, Is.EqualTo(25), "35 min after the start, net of the 10 min tolerance");
            Assert.That(record.Status, Is.EqualTo(AttendanceStatus.Late), "a worked day with lateness is reported as Retard");
            Assert.That(record.WorkedHours, Is.EqualTo(7.42m).Within(0.01m));
        }

        [Test]
        public void Save_AbsentDay_CarriesNoHours()
        {
            _service.Save(new AttendanceRecord
            {
                EmployeeId = _employeeId,
                WorkDate = new DateTime(Year, 3, 5),
                Status = AttendanceStatus.Absent,
                CheckIn = "08:00",
                CheckOut = "17:00"
            });

            AttendanceRecord record = _service.Get(_employeeId, new DateTime(Year, 3, 5));
            Assert.That(record.WorkedHours, Is.EqualTo(0m));
            Assert.That(record.OvertimeHours, Is.EqualTo(0m));
            Assert.That(record.LateMinutes, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- one row per day

        [Test]
        public void Save_SameDayTwice_UpdatesInsteadOfDuplicating()
        {
            var date = new DateTime(Year, 3, 6);
            _service.Save(Day(_employeeId, date, "08:00", "16:00"));
            _service.Save(Day(_employeeId, date, "08:00", "18:00"));

            IReadOnlyList<AttendanceRecord> day = _service.GetCompanyDay(_companyId, date);
            Assert.That(day.Count, Is.EqualTo(1), "one record per employee and day");
            Assert.That(day[0].WorkedHours, Is.EqualTo(10m), "the second save updates the first");
        }

        [Test]
        public void SaveMany_WritesTheWholeDayAtOnce()
        {
            var date = new DateTime(Year, 3, 9);
            Result result = _service.SaveMany(new[]
            {
                Day(_employeeId, date, "08:00", "16:00"),
                Day(_otherEmployeeId, date, "08:45", "16:00")
            });

            Assert.That(result.IsSuccess, Is.True, result.Error);

            IReadOnlyList<AttendanceRecord> day = _service.GetCompanyDay(_companyId, date);
            Assert.That(day.Count, Is.EqualTo(2));
        }

        // ---------------------------------------------------------------- shared data

        [Test]
        public void GetCompanyDay_ScopesThroughTheSharedEmployeeTable()
        {
            long otherCompanyId;
            long strangerId;
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                otherCompanyId = uow.Companies.Insert(new Company { NameFr = "EURL Autre", Nif = "111111111111111" });
                strangerId = uow.Employees.Insert(NewEmployee(otherCompanyId, "ZIANI", "Amine"));
                uow.Commit();
            }

            var date = new DateTime(Year, 3, 10);
            _service.Save(Day(_employeeId, date, "08:00", "16:00"));
            _service.Save(Day(strangerId, date, "08:00", "16:00"));

            Assert.That(_service.GetCompanyDay(_companyId, date).Count, Is.EqualTo(1));
            Assert.That(_service.GetCompanyDay(otherCompanyId, date).Count, Is.EqualTo(1),
                "attendance is scoped by joining the shared Employees table — the company is never stored twice");
        }

        // ---------------------------------------------------------------- monthly summary

        [Test]
        public void GetMonthlySummary_AggregatesTheFiguresPayrollConsumes()
        {
            _service.Save(Day(_employeeId, new DateTime(Year, 4, 1), "08:00", "16:00"));   // 8 h
            _service.Save(Day(_employeeId, new DateTime(Year, 4, 2), "08:00", "18:00"));   // 10 h → 2 h supp
            _service.Save(Day(_employeeId, new DateTime(Year, 4, 3), "08:20", "16:00"));   // 10 min late (20 − 10)
            _service.Save(new AttendanceRecord { EmployeeId = _employeeId, WorkDate = new DateTime(Year, 4, 6), Status = AttendanceStatus.Absent });
            _service.Save(new AttendanceRecord { EmployeeId = _employeeId, WorkDate = new DateTime(Year, 4, 7), Status = AttendanceStatus.Leave });

            AttendanceSummary summary = _service.GetMonthlySummary(_employeeId, Year, 4);

            Assert.That(summary.RecordedDays, Is.EqualTo(5));
            Assert.That(summary.AbsentDays, Is.EqualTo(1));
            Assert.That(summary.LeaveDays, Is.EqualTo(1));
            Assert.That(summary.LateCount, Is.EqualTo(1));
            Assert.That(summary.LateMinutes, Is.EqualTo(10));
            Assert.That(summary.OvertimeHours, Is.EqualTo(2m));
            Assert.That(summary.WorkedHours, Is.EqualTo(25.67m).Within(0.02m));
            Assert.That(summary.PresentDays, Is.EqualTo(3), "worked days, late ones included");
        }

        [Test]
        public void GetMonthlySummary_IgnoresOtherMonths()
        {
            _service.Save(Day(_employeeId, new DateTime(Year, 4, 30), "08:00", "16:00"));
            _service.Save(Day(_employeeId, new DateTime(Year, 5, 1), "08:00", "16:00"));

            Assert.That(_service.GetMonthlySummary(_employeeId, Year, 4).RecordedDays, Is.EqualTo(1));
            Assert.That(_service.GetMonthlySummary(_employeeId, Year, 5).RecordedDays, Is.EqualTo(1));
        }

        [Test]
        public void GetCompanyMonthlySummary_ReturnsEveryEmployeeWithRecords()
        {
            _service.Save(Day(_employeeId, new DateTime(Year, 6, 1), "08:00", "16:00"));
            _service.Save(Day(_otherEmployeeId, new DateTime(Year, 6, 1), "08:00", "17:00"));

            IReadOnlyList<AttendanceSummary> rows = _service.GetCompanyMonthlySummary(_companyId, Year, 6);

            Assert.That(rows.Count, Is.EqualTo(2));
            foreach (AttendanceSummary row in rows)
            {
                Assert.That(row.EmployeeName, Is.Not.Null.And.Not.Empty, "names come from the shared employee table");
                Assert.That(row.RecordedDays, Is.EqualTo(1));
            }
        }

        [Test]
        public void EmptyMonth_ReturnsZeroedSummary_SoPayrollKeepsItsDefaults()
        {
            AttendanceSummary summary = _service.GetMonthlySummary(_employeeId, Year, 12);

            Assert.That(summary, Is.Not.Null);
            Assert.That(summary.RecordedDays, Is.EqualTo(0));
            Assert.That(summary.WorkedHours, Is.EqualTo(0m));
            Assert.That(summary.AbsentDays, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- delete + settings

        [Test]
        public void Delete_RemovesTheDayFromTheSummary()
        {
            var date = new DateTime(Year, 7, 1);
            Result<long> saved = _service.Save(Day(_employeeId, date, "08:00", "16:00"));

            Result deleted = _service.Delete(saved.Value);

            Assert.That(deleted.IsSuccess, Is.True, deleted.Error);
            Assert.That(_service.Get(_employeeId, date), Is.Null);
            Assert.That(_service.GetMonthlySummary(_employeeId, Year, 7).RecordedDays, Is.EqualTo(0));
        }

        [Test]
        public void Delete_ThenRecordTheSameDayAgain_Succeeds()
        {
            var date = new DateTime(Year, 7, 2);
            Result<long> saved = _service.Save(Day(_employeeId, date, "08:00", "16:00"));
            _service.Delete(saved.Value);

            Result<long> again = _service.Save(Day(_employeeId, date, "08:00", "18:00"));

            Assert.That(again.IsSuccess, Is.True, again.Error);
            Assert.That(_service.Get(_employeeId, date).WorkedHours, Is.EqualTo(10m));
            Assert.That(_service.GetMonthlySummary(_employeeId, Year, 7).RecordedDays, Is.EqualTo(1),
                "the deleted row must not resurface in the totals");
        }

        [Test]
        public void Settings_RoundTripAndDriveTheCalculation()
        {
            _service.SaveSettings(new AttendanceSettings
            {
                StandardStart = "09:00",
                StandardHours = 6m,
                LateToleranceMinutes = 0
            });

            AttendanceSettings reloaded = _service.GetSettings();
            Assert.That(reloaded.StandardStart, Is.EqualTo("09:00"));
            Assert.That(reloaded.StandardHours, Is.EqualTo(6m));
            Assert.That(reloaded.LateToleranceMinutes, Is.EqualTo(0));

            _service.Save(Day(_employeeId, new DateTime(Year, 8, 3), "09:05", "16:00"));

            AttendanceRecord record = _service.Get(_employeeId, new DateTime(Year, 8, 3));
            Assert.That(record.LateMinutes, Is.EqualTo(5), "the new start time and zero tolerance apply");
            Assert.That(record.OvertimeHours, Is.EqualTo(0.92m).Within(0.02m), "against the new 6 h standard day");
        }

        [Test]
        public void Save_InvalidTime_IsRejected()
        {
            Result<long> result = _service.Save(new AttendanceRecord
            {
                EmployeeId = _employeeId,
                WorkDate = new DateTime(Year, 9, 1),
                Status = AttendanceStatus.Present,
                CheckIn = "25:99",
                CheckOut = "16:00"
            });

            Assert.That(result.IsFailure, Is.True, "an unparsable time must not silently become 0 h");
        }

        [Test]
        public void Save_FutureDate_IsRejected()
        {
            Result<long> result = _service.Save(Day(_employeeId, DateTime.Today.AddDays(1), "08:00", "16:00"));

            Assert.That(result.IsFailure, Is.True, "pointage cannot be recorded in advance");
        }

        [Test]
        public void Save_UnknownEmployee_IsRejected()
        {
            Result<long> result = _service.Save(Day(999999, new DateTime(Year, 9, 2), "08:00", "16:00"));

            Assert.That(result.IsFailure, Is.True, "attendance always points at a real, shared employee");
        }

        private static AttendanceRecord Day(long employeeId, DateTime date, string checkIn, string checkOut)
        {
            return new AttendanceRecord
            {
                EmployeeId = employeeId,
                WorkDate = date,
                Status = AttendanceStatus.Present,
                CheckIn = checkIn,
                CheckOut = checkOut
            };
        }
    }
}
