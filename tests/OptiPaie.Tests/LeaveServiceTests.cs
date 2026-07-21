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
    /// Leave module — integration tests against a real SQLite file. Beyond the leave
    /// rules themselves they prove the ecosystem promise: approving a request makes
    /// the days appear in Attendance (and therefore in payroll) with no import step,
    /// and cancelling takes them back out without touching manually recorded days.
    /// </summary>
    [TestFixture]
    public sealed class LeaveServiceTests
    {
        private static readonly int Year = DateTime.Today.Year - 1;

        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private ILeaveService _service;
        private IAttendanceService _attendance;

        private long _companyId;
        private long _employeeId;
        private long _otherEmployeeId;

        /// <summary>A Sunday — the start of the Algerian working week.</summary>
        private DateTime _weekStart;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-leave-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new LeaveService(_unitOfWorkFactory);
            _attendance = new AttendanceService(_unitOfWorkFactory);

            SeedSharedData();

            _weekStart = FirstDayOfWeek(Year, 6, DayOfWeek.Sunday);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        private void SeedSharedData()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
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
                ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year - 4, 1, 1),
                BaseSalary = 50000m,
                IsActive = true
            };
        }

        private static DateTime FirstDayOfWeek(int year, int month, DayOfWeek dayOfWeek)
        {
            var day = new DateTime(year, month, 1);
            while (day.DayOfWeek != dayOfWeek) day = day.AddDays(1);
            return day;
        }

        // ---------------------------------------------------------------- day counting

        [Test]
        public void Save_CountsWorkingDaysOnly_FridayAndSaturdayExcluded()
        {
            // A full calendar week: Sunday → Saturday = 7 days, 5 of them leave days.
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(6), LeaveType.Annual));

            Assert.That(saved.IsSuccess, Is.True, saved.Error);
            Assert.That(_service.Get(saved.Value).Days, Is.EqualTo(5m));
        }

        [Test]
        public void CountDays_MatchesTheStoredValue()
        {
            Assert.That(_service.CountDays(_weekStart, _weekStart.AddDays(6)), Is.EqualTo(5m));
            Assert.That(_service.CountDays(_weekStart, _weekStart), Is.EqualTo(1m));
        }

        [Test]
        public void Save_RestDaysOnly_IsRejected()
        {
            DateTime friday = _weekStart.AddDays(5);
            Result<long> result = _service.Save(Request(_employeeId, friday, friday.AddDays(1), LeaveType.Annual));

            Assert.That(result.IsFailure, Is.True, "Friday + Saturday contain no leave day");
        }

        [Test]
        public void Save_EndBeforeStart_IsRejected()
        {
            Result<long> result = _service.Save(
                Request(_employeeId, _weekStart.AddDays(3), _weekStart, LeaveType.Annual));

            Assert.That(result.IsFailure, Is.True);
        }

        [Test]
        public void Save_UnknownEmployee_IsRejected()
        {
            Result<long> result = _service.Save(Request(999999, _weekStart, _weekStart.AddDays(2), LeaveType.Annual));

            Assert.That(result.IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- overlap

        [Test]
        public void Save_OverlappingLiveRequest_IsRejected()
        {
            _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(3), LeaveType.Annual));

            Result<long> second = _service.Save(
                Request(_employeeId, _weekStart.AddDays(2), _weekStart.AddDays(4), LeaveType.Sick));

            Assert.That(second.IsFailure, Is.True, "two live requests may not cover the same day");
        }

        [Test]
        public void Save_OverlappingRejectedRequest_IsAllowed()
        {
            Result<long> first = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(3), LeaveType.Annual));
            _service.Reject(first.Value, "Effectif insuffisant");

            Result<long> second = _service.Save(
                Request(_employeeId, _weekStart.AddDays(2), _weekStart.AddDays(4), LeaveType.Annual));

            Assert.That(second.IsSuccess, Is.True, second.Error);
        }

        [Test]
        public void Save_SamePeriodForAnotherEmployee_IsAllowed()
        {
            _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(3), LeaveType.Annual));

            Result<long> other = _service.Save(
                Request(_otherEmployeeId, _weekStart, _weekStart.AddDays(3), LeaveType.Annual));

            Assert.That(other.IsSuccess, Is.True, other.Error);
        }

        // ---------------------------------------------------------------- cross-module sync

        [Test]
        public void ApprovedLeaveAndManualPointage_ShareOneDayRepresentation()
        {
            // Regression: the two modules used to store the same calendar day as two
            // different texts, so a leave day was invisible to the attendance lookup.
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart, LeaveType.Annual));
            _service.Approve(saved.Value, null);

            _attendance.Save(new AttendanceRecord
            {
                EmployeeId = _otherEmployeeId,
                WorkDate = _weekStart,
                Status = AttendanceStatus.Present,
                CheckIn = "08:00",
                CheckOut = "16:00"
            });

            using (var raw = new System.Data.SQLite.SQLiteConnection("Data Source=" + Path.Combine(_directory, "test.db")))
            {
                raw.Open();
                List<string> stored = Dapper.SqlMapper.Query<string>(raw,
                    "SELECT DISTINCT CAST(WorkDate AS TEXT) FROM AttendanceRecords;").ToList();

                Assert.That(stored.Count, Is.EqualTo(1),
                    "one calendar day must have exactly one stored representation, whichever module wrote it");
            }
        }

        [Test]
        public void Approve_WritesTheDaysIntoAttendance()
        {
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(2), LeaveType.Annual));

            Result approved = _service.Approve(saved.Value, "Accordé");

            Assert.That(approved.IsSuccess, Is.True, approved.Error);
            for (int i = 0; i <= 2; i++)
            {
                AttendanceRecord day = _attendance.Get(_employeeId, _weekStart.AddDays(i));
                Assert.That(day, Is.Not.Null, "day " + i + " must appear in attendance");
                Assert.That(day.Status, Is.EqualTo(AttendanceStatus.Leave));
                Assert.That(day.WorkedHours, Is.EqualTo(0m));
            }
        }

        [Test]
        public void Approve_UnpaidLeave_IsRecordedAsAbsentSoPayrollDeductsIt()
        {
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(1), LeaveType.Unpaid));

            _service.Approve(saved.Value, null);

            AttendanceRecord day = _attendance.Get(_employeeId, _weekStart);
            Assert.That(day.Status, Is.EqualTo(AttendanceStatus.Absent));

            AttendanceSummary summary = _attendance.GetMonthlySummary(_employeeId, _weekStart.Year, _weekStart.Month);
            Assert.That(summary.AbsentDays, Is.EqualTo(2), "payroll reduces the worked days accordingly");
        }

        [Test]
        public void Approve_SkipsRestDays()
        {
            // Sunday → Saturday: Friday and Saturday must NOT appear in attendance.
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(6), LeaveType.Annual));
            _service.Approve(saved.Value, null);

            Assert.That(_attendance.Get(_employeeId, _weekStart.AddDays(5)), Is.Null, "Friday is a rest day");
            Assert.That(_attendance.Get(_employeeId, _weekStart.AddDays(6)), Is.Null, "Saturday is a rest day");
        }

        [Test]
        public void Cancel_RemovesTheAttendanceDaysAgain()
        {
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(2), LeaveType.Annual));
            _service.Approve(saved.Value, null);

            Result cancelled = _service.Cancel(saved.Value, "Reporté");

            Assert.That(cancelled.IsSuccess, Is.True, cancelled.Error);
            for (int i = 0; i <= 2; i++)
            {
                Assert.That(_attendance.Get(_employeeId, _weekStart.AddDays(i)), Is.Null,
                    "the leave day must disappear from attendance");
            }
        }

        [Test]
        public void Cancel_LeavesManuallyRecordedDaysAlone()
        {
            // A day recorded by hand in the Attendance module, inside the leave period.
            _attendance.Save(new AttendanceRecord
            {
                EmployeeId = _employeeId,
                WorkDate = _weekStart,
                Status = AttendanceStatus.Present,
                CheckIn = "08:00",
                CheckOut = "16:00",
                Notes = "Pointage manuel"
            });

            Result<long> saved = _service.Save(Request(_employeeId, _weekStart.AddDays(1), _weekStart.AddDays(2), LeaveType.Annual));
            _service.Approve(saved.Value, null);
            _service.Cancel(saved.Value, null);

            AttendanceRecord manual = _attendance.Get(_employeeId, _weekStart);
            Assert.That(manual, Is.Not.Null, "a manually recorded day is never removed by the leave module");
            Assert.That(manual.Status, Is.EqualTo(AttendanceStatus.Present));
        }

        [Test]
        public void Delete_OfAnApprovedRequest_RemovesItsAttendanceDays()
        {
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(1), LeaveType.Annual));
            _service.Approve(saved.Value, null);

            _service.Delete(saved.Value);

            Assert.That(_service.Get(saved.Value), Is.Null);
            Assert.That(_attendance.Get(_employeeId, _weekStart), Is.Null);
        }

        [Test]
        public void CancelThenRequestTheSamePeriodAgain_Succeeds()
        {
            Result<long> first = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(2), LeaveType.Annual));
            _service.Approve(first.Value, null);
            _service.Cancel(first.Value, null);

            Result<long> second = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(2), LeaveType.Annual));
            Assert.That(second.IsSuccess, Is.True, second.Error);

            Result approved = _service.Approve(second.Value, null);
            Assert.That(approved.IsSuccess, Is.True, approved.Error);
            Assert.That(_attendance.Get(_employeeId, _weekStart), Is.Not.Null);
        }

        // ---------------------------------------------------------------- lifecycle

        [Test]
        public void Save_OfAnApprovedRequest_IsRejected()
        {
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(1), LeaveType.Annual));
            _service.Approve(saved.Value, null);

            LeaveRequest request = _service.Get(saved.Value);
            request.EndDate = _weekStart.AddDays(3);

            Result<long> edited = _service.Save(request);
            Assert.That(edited.IsFailure, Is.True, "an approved request is frozen");
        }

        [Test]
        public void Reject_ThenApprove_IsRejected()
        {
            Result<long> saved = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(1), LeaveType.Annual));
            _service.Reject(saved.Value, "Non");

            Result approved = _service.Approve(saved.Value, null);

            Assert.That(approved.IsFailure, Is.True);
            Assert.That(_attendance.Get(_employeeId, _weekStart), Is.Null, "a refusal writes nothing");
        }

        // ---------------------------------------------------------------- balance

        [Test]
        public void GetBalance_FullYearOfService_Gives30Days()
        {
            LeaveBalance balance = _service.GetBalance(_employeeId, Year);

            Assert.That(balance.Entitlement, Is.EqualTo(30m), "2,5 days per month worked, capped at 30");
            Assert.That(balance.Taken, Is.EqualTo(0m));
            Assert.That(balance.Remaining, Is.EqualTo(30m));
        }

        [Test]
        public void GetBalance_CountsApprovedAnnualLeaveOnly()
        {
            Result<long> annual = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(4), LeaveType.Annual));
            _service.Approve(annual.Value, null);

            Result<long> pending = _service.Save(
                Request(_employeeId, _weekStart.AddDays(7), _weekStart.AddDays(8), LeaveType.Annual));

            Result<long> sick = _service.Save(
                Request(_employeeId, _weekStart.AddDays(14), _weekStart.AddDays(15), LeaveType.Sick));
            _service.Approve(sick.Value, null);

            LeaveBalance balance = _service.GetBalance(_employeeId, Year);

            Assert.That(balance.Taken, Is.EqualTo(5m), "Sunday → Thursday");
            Assert.That(balance.Remaining, Is.EqualTo(25m));
            Assert.That(balance.Pending, Is.EqualTo(2m), "pending is shown, not deducted");
            Assert.That(balance.OtherLeaveDays, Is.EqualTo(2m), "sick leave never touches the annual balance");
            Assert.That(pending.IsSuccess, Is.True);
        }

        [Test]
        public void GetBalance_TracksUnpaidDaysSeparately()
        {
            Result<long> unpaid = _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(1), LeaveType.Unpaid));
            _service.Approve(unpaid.Value, null);

            LeaveBalance balance = _service.GetBalance(_employeeId, Year);

            Assert.That(balance.UnpaidDays, Is.EqualTo(2m));
            Assert.That(balance.Taken, Is.EqualTo(0m), "unpaid leave is not annual leave");
        }

        [Test]
        public void GetBalance_HiredMidYear_ProRatesTheEntitlement()
        {
            long lateHire;
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                Employee employee = NewEmployee(_companyId, "MEZIANE", "Nadia");
                employee.HireDate = new DateTime(Year, 7, 1); // six months of the year
                lateHire = uow.Employees.Insert(employee);
                uow.Commit();
            }

            LeaveBalance balance = _service.GetBalance(lateHire, Year);

            Assert.That(balance.Entitlement, Is.EqualTo(15m), "6 months × 2,5 days");
        }

        [Test]
        public void GetCompanyBalances_ReturnsEveryEmployeeOfTheSharedCompany()
        {
            IReadOnlyList<LeaveBalance> balances = _service.GetCompanyBalances(_companyId, Year);

            Assert.That(balances.Count, Is.EqualTo(2));
            Assert.That(balances.All(b => !string.IsNullOrWhiteSpace(b.EmployeeName)), Is.True,
                "names come from the shared employee table");
        }

        [Test]
        public void GetBalance_CrossYearRequest_CountsOnlyTheDaysInsideTheYear()
        {
            // 28 December → 6 January: only the December part belongs to this year.
            var start = new DateTime(Year, 12, 28);
            var end = new DateTime(Year + 1, 1, 6);

            Result<long> saved = _service.Save(Request(_employeeId, start, end, LeaveType.Annual));
            Assert.That(saved.IsSuccess, Is.True, saved.Error);
            _service.Approve(saved.Value, null);

            decimal inDecember = _service.CountDays(start, new DateTime(Year, 12, 31));
            LeaveBalance balance = _service.GetBalance(_employeeId, Year);

            Assert.That(balance.Taken, Is.EqualTo(inDecember));
            Assert.That(balance.Taken, Is.LessThan(_service.CountDays(start, end)));
        }

        // ---------------------------------------------------------------- listing + settings

        [Test]
        public void GetByCompanyYear_ListsTheRequestsOfTheYear()
        {
            _service.Save(Request(_employeeId, _weekStart, _weekStart.AddDays(1), LeaveType.Annual));
            _service.Save(Request(_otherEmployeeId, _weekStart, _weekStart.AddDays(1), LeaveType.Sick));

            IReadOnlyList<LeaveRequest> requests = _service.GetByCompanyYear(_companyId, Year);

            Assert.That(requests.Count, Is.EqualTo(2));
            Assert.That(_service.GetByCompanyYear(_companyId, Year - 2).Count, Is.EqualTo(0));
        }

        [Test]
        public void Settings_RoundTripAndDriveTheCalculation()
        {
            Result saved = _service.SaveSettings(new LeaveSettings
            {
                DaysPerMonth = 2m,
                AnnualCap = 24m,
                ExcludeRestDays = false
            });
            Assert.That(saved.IsSuccess, Is.True, saved.Error);

            LeaveSettings reloaded = _service.GetSettings();
            Assert.That(reloaded.DaysPerMonth, Is.EqualTo(2m));
            Assert.That(reloaded.AnnualCap, Is.EqualTo(24m));
            Assert.That(reloaded.ExcludeRestDays, Is.False);

            Assert.That(_service.CountDays(_weekStart, _weekStart.AddDays(6)), Is.EqualTo(7m),
                "rest days now count as leave days");
            Assert.That(_service.GetBalance(_employeeId, Year).Entitlement, Is.EqualTo(24m),
                "12 months × 2 days, capped at 24");
        }

        private static LeaveRequest Request(long employeeId, DateTime start, DateTime end, LeaveType type)
        {
            return new LeaveRequest
            {
                EmployeeId = employeeId,
                Type = type,
                StartDate = start,
                EndDate = end,
                Reason = "Test"
            };
        }
    }
}
