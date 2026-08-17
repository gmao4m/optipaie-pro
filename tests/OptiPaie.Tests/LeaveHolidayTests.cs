using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>Holidays screen backend: round-trip, civil pre-fill, and a holiday inside a leave period is not counted.</summary>
    [TestFixture]
    public sealed class LeaveHolidayTests
    {
        private static readonly int Year = DateTime.Today.Year - 1;

        private string _directory;
        private IUnitOfWorkFactory _uow;
        private HolidayService _holidays;
        private LeaveService _leave;
        private long _companyId;
        private long _employeeId;
        private DateTime _weekStart;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-holiday-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _holidays = new HolidayService(_uow);
            _leave = new LeaveService(_uow);

            using (IUnitOfWork uow = _uow.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Fériés", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId, LastNameFr = "BENALI", FirstNameFr = "Karim",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(Year - 4, 1, 1), BaseSalary = 40000m, IsActive = true
                });
                uow.Commit();
            }

            _weekStart = FirstDayOfWeek(Year, 6, DayOfWeek.Sunday);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        private static DateTime FirstDayOfWeek(int year, int month, DayOfWeek dow)
        {
            var day = new DateTime(year, month, 1);
            while (day.DayOfWeek != dow) day = day.AddDays(1);
            return day;
        }

        [Test]
        public void AddAndList_RoundTrips()
        {
            Assert.That(_holidays.Add(new Holiday { CompanyId = _companyId, HolidayDate = new DateTime(Year, 6, 3), NameAr = "عيد", IsReligious = true }).IsSuccess, Is.True);
            var list = _holidays.GetForYear(_companyId, Year);
            Assert.That(list.Any(h => h.NameAr == "عيد" && h.HolidayDate.Date == new DateTime(Year, 6, 3)), Is.True);
        }

        [Test]
        public void EnsureCivilForYear_SeedsFive_ThenIdempotent()
        {
            Assert.That(_holidays.EnsureCivilForYear(_companyId, Year), Is.EqualTo(5), "les 5 fêtes civiles fixes");
            Assert.That(_holidays.EnsureCivilForYear(_companyId, Year), Is.EqualTo(0), "idempotent : rien à ajouter la 2e fois");
        }

        [Test]
        public void AddRequiresName()
        {
            var r = _holidays.Add(new Holiday { CompanyId = _companyId, HolidayDate = new DateTime(Year, 6, 3), NameAr = " " });
            Assert.That(r.IsFailure, Is.True);
            Assert.That(r.ErrorCode, Is.EqualTo("Holiday_NameRequired"));
        }

        [Test]
        public void HolidayEnteredInLeavePeriod_IsNotCounted()
        {
            // Enable the exclusion (opt-in), then enter a holiday on a working Tuesday.
            LeaveSettings s = _leave.GetSettings(_companyId);
            s.ExcludeHolidays = true;
            Assert.That(_leave.SaveSettings(_companyId, s).IsSuccess, Is.True);

            DateTime from = _weekStart.AddDays(1);   // Monday
            DateTime to = _weekStart.AddDays(4);      // Thursday → 4 working days
            _holidays.Add(new Holiday { CompanyId = _companyId, HolidayDate = _weekStart.AddDays(2), NameAr = "عيد ديني", IsReligious = true });

            var req = new LeaveRequest { EmployeeId = _employeeId, Type = LeaveType.Annual, StartDate = from, EndDate = to };
            Assert.That(_leave.Preview(req).Days, Is.EqualTo(3m), "le férié saisi dans la période n'est pas décompté (4 − 1)");
        }
    }
}
