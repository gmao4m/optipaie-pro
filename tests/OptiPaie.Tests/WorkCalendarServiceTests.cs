using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 2 — the work calendar + proration basis. Every expected value is hand-computed from
    /// the law of the weekly pattern (Algerian default Sun–Thu work, Fri–Sat rest) and plain
    /// arithmetic (RULE 0). The default basis must reproduce today's result exactly.
    /// </summary>
    [TestFixture]
    public sealed class WorkCalendarServiceTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private WorkCalendarService _sut;
        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-wc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);

            var companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            var holidays = new HolidayService(_uow, AccessContext.Unrestricted);
            _sut = new WorkCalendarService(_uow, holidays, AccessContext.Unrestricted);
            _companyId = companies.Create(new Company { NameFr = "SARL Cal", Nif = "000000000000000" }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private long AddEmployee(DateTime hire, DateTime? exit = null, decimal baseSalary = 30000m)
        {
            var employees = new EmployeeService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            return employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "WORK", FirstNameFr = "Cal", Poste = "Agent",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = hire, ExitDate = exit, IsActive = exit == null,
                BaseSalary = baseSalary
            }).Value;
        }

        // ---------------------------------------------------------------- proration maths

        [Test]
        public void Default_Basis_Divisor_Is_The_Months_Real_Days()
        {
            Assert.That(_sut.Denominator(ProrationBasis.MonthDays, 2026, 2, 0), Is.EqualTo(28m), "Feb 2026");
            Assert.That(_sut.Denominator(ProrationBasis.MonthDays, 2024, 2, 0), Is.EqualTo(29m), "Feb 2024 leap");
            Assert.That(_sut.Denominator(ProrationBasis.MonthDays, 2026, 6, 0), Is.EqualTo(30m), "June");
            Assert.That(_sut.Denominator(ProrationBasis.MonthDays, 2026, 7, 0), Is.EqualTo(31m), "July");
        }

        [Test]
        public void Default_Deduction_Reproduces_Todays_Result_28_29_30_31()
        {
            // today = base × absentDays ÷ the month's real days.
            decimal b = 30000m; int a = 3;
            Assert.That(_sut.Deduction(b, a, _sut.Denominator(ProrationBasis.MonthDays, 2026, 2, 0)), Is.EqualTo(3214.29m)); // ÷28
            Assert.That(_sut.Deduction(b, a, _sut.Denominator(ProrationBasis.MonthDays, 2024, 2, 0)), Is.EqualTo(3103.45m)); // ÷29
            Assert.That(_sut.Deduction(b, a, _sut.Denominator(ProrationBasis.MonthDays, 2026, 6, 0)), Is.EqualTo(3000.00m)); // ÷30
            Assert.That(_sut.Deduction(b, a, _sut.Denominator(ProrationBasis.MonthDays, 2026, 7, 0)), Is.EqualTo(2903.23m)); // ÷31
        }

        [Test]
        public void Three_Bases_Give_Three_Different_Results_For_The_Same_Month()
        {
            // July 2026 (31 days), employee employed all month → 22 workable days (Sun–Thu, no holidays
            // except 5 Jul which falls on a Sunday → 22 − 1 = 21? see below). Use explicit divisors here.
            long emp = AddEmployee(new DateTime(2024, 1, 1));
            int workable = _sut.GetWorkableDays(emp, 2026, 7); // 21 (one civil holiday on a work day)
            decimal b = 30000m; int a = 3;

            decimal monthDays = _sut.Deduction(b, a, _sut.Denominator(ProrationBasis.MonthDays, 2026, 7, workable));      // ÷31
            decimal thirty = _sut.Deduction(b, a, _sut.Denominator(ProrationBasis.FixedThirtieths, 2026, 7, workable));  // ÷30
            decimal working = _sut.Deduction(b, a, _sut.Denominator(ProrationBasis.WorkingDays, 2026, 7, workable));     // ÷21

            Assert.That(monthDays, Is.EqualTo(2903.23m));
            Assert.That(thirty, Is.EqualTo(3000.00m));
            Assert.That(working, Is.EqualTo(Math.Round(b * a / workable, 2, MidpointRounding.AwayFromZero)));
            Assert.That(new[] { monthDays, thirty, working }.Distinct().Count(), Is.EqualTo(3), "three distinct results");
        }

        // ---------------------------------------------------------------- workable days

        [Test]
        public void WorkableDays_FullMonth_Default_Pattern()
        {
            // June 2026 (1 Jun = Monday). Fri/Sat rest: 5,6,12,13,19,20,26,27 = 8 → 30 − 8 = 22. No June holiday.
            long emp = AddEmployee(new DateTime(2024, 1, 1));
            Assert.That(_sut.GetWorkableDays(emp, 2026, 6), Is.EqualTo(22));
        }

        [Test]
        public void WorkableDays_MidMonthHire_And_Departure()
        {
            // Hire 10 Jun 2026 → days 10..30. Rest in range: 12,13,19,20,26,27 = 6 → 21 − 6 = 15.
            long hired = AddEmployee(new DateTime(2026, 6, 10));
            Assert.That(_sut.GetWorkableDays(hired, 2026, 6), Is.EqualTo(15));

            // Exit 15 Jun 2026 → days 1..15. Rest in range: 5,6,12,13 = 4 → 15 − 4 = 11.
            long left = AddEmployee(new DateTime(2024, 1, 1), new DateTime(2026, 6, 15));
            Assert.That(_sut.GetWorkableDays(left, 2026, 6), Is.EqualTo(11));
        }

        [Test]
        public void WorkableDays_ExcludesHolidays()
        {
            // July 2026 full = 22 work days (Fri 3,10,17,24,31; Sat 4,11,18,25 = 9 rest → 31 − 9 = 22).
            // 5 Jul (Independence, fixed civil) is a Sunday = a work day → excluded → 21.
            long emp = AddEmployee(new DateTime(2024, 1, 1));
            Assert.That(_sut.GetWorkableDays(emp, 2026, 7), Is.EqualTo(21));

            // A manually added company holiday on a work day (6 Jul = Monday) drops it to 20.
            using (IUnitOfWork uow = _uow.Create())
            {
                uow.Holidays.Insert(new Holiday { CompanyId = _companyId, HolidayDate = new DateTime(2026, 7, 6), NameAr = "اختبار", IsReligious = false });
            }
            Assert.That(_sut.GetWorkableDays(emp, 2026, 7), Is.EqualTo(20));
        }

        // ---------------------------------------------------------------- per-employee pattern

        private long AddScheduled(DateTime hire, bool mon, bool tue, bool wed, bool thu, bool fri, bool sat, bool sun, decimal baseSalary = 30000m)
        {
            var employees = new EmployeeService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            return employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "GUARD", FirstNameFr = "Shift", Poste = "Gardien",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = hire, IsActive = true, BaseSalary = baseSalary,
                UsesOwnSchedule = true,
                SchedMon = mon, SchedTue = tue, SchedWed = wed, SchedThu = thu, SchedFri = fri, SchedSat = sat, SchedSun = sun
            }).Value;
        }

        [Test]
        public void WorkableDays_PerEmployeeSchedule_GuardWorksEveryDay()
        {
            // June 2026, no civil holiday. Default employee = 22 (Fri/Sat rest). A guard who works all
            // seven days has his Fridays and Saturdays as work days → the full 30. His days due are HIS.
            long normal = AddEmployee(new DateTime(2024, 1, 1));
            long guard = AddScheduled(new DateTime(2024, 1, 1), true, true, true, true, true, true, true);

            Assert.That(_sut.GetWorkableDays(normal, 2026, 6), Is.EqualTo(22), "default employee unchanged");
            Assert.That(_sut.GetWorkableDays(guard, 2026, 6), Is.EqualTo(30), "guard works Fri+Sat too");
        }

        [Test]
        public void WorkableDays_PerEmployeeSchedule_HonoursTheExactPattern()
        {
            // A guard who works Sun–Fri (Saturday off). June 2026 Saturdays: 6,13,20,27 = 4 rest → 30 − 4 = 26.
            long guard = AddScheduled(new DateTime(2024, 1, 1), true, true, true, true, true, false, true);
            Assert.That(_sut.GetWorkableDays(guard, 2026, 6), Is.EqualTo(26));
        }

        [Test]
        public void WorkableDays_PerEmployeeSchedule_StillExcludesHolidays()
        {
            // Guard works all seven days in July 2026; 5 Jul (civil) is a Sunday that he would work → excluded.
            long guard = AddScheduled(new DateTime(2024, 1, 1), true, true, true, true, true, true, true);
            Assert.That(_sut.GetWorkableDays(guard, 2026, 7), Is.EqualTo(30), "31 days − 1 civil holiday");
        }

        [Test]
        public void ResolveForPayroll_WorkingDaysBasis_PaysGuardOnHisOwnDivisor()
        {
            // Company prorates on worked days. A one-day unjustified absence costs each person a slice of
            // THEIR own days due: the guard's divisor is 30 (he works all week), the default employee's is 22.
            CompanyWorkCalendar cal = _sut.GetSettings(_companyId);
            cal.ProrationBasis = ProrationBasis.WorkingDays;
            Assert.That(_sut.SaveSettings(cal).IsSuccess, Is.True);

            long normal = AddEmployee(new DateTime(2024, 1, 1));
            long guard = AddScheduled(new DateTime(2024, 1, 1), true, true, true, true, true, true, true);
            var summary = new AttendanceSummary { Year = 2026, Month = 6, RecordedDays = 1, AbsentDays = 1 };

            ProrationResult n = _sut.ResolveForPayroll(_companyId, normal, 2026, 6, summary, true);
            ProrationResult g = _sut.ResolveForPayroll(_companyId, guard, 2026, 6, summary, true);

            Assert.That(n.WorkableDays, Is.EqualTo(22m));
            Assert.That(n.WorkedDays, Is.EqualTo(21m));
            Assert.That(g.WorkableDays, Is.EqualTo(30m), "guard's days due are his own");
            Assert.That(g.WorkedDays, Is.EqualTo(29m));
        }

        // ---------------------------------------------------------------- settings

        [Test]
        public void GetSettings_ReturnsTodaysDefaults_WhenNoneSaved()
        {
            CompanyWorkCalendar c = _sut.GetSettings(_companyId);
            Assert.That(c.ProrationBasis, Is.EqualTo(ProrationBasis.MonthDays), "default basis = today");
            Assert.That(c.RestDayRate, Is.EqualTo(1m), "no premium by default = today");
            Assert.That(c.WorkFri, Is.False); Assert.That(c.WorkSat, Is.False);
            Assert.That(c.WorkSun, Is.True); Assert.That(c.WorkThu, Is.True);
        }

        [Test]
        public void SaveSettings_RoundTrips()
        {
            var c = _sut.GetSettings(_companyId);
            c.ProrationBasis = ProrationBasis.WorkingDays;
            c.WorkFri = true; c.RestDayRate = 1.5m; c.HalfDayHours = 4m;
            Assert.That(_sut.SaveSettings(c).IsSuccess, Is.True);

            CompanyWorkCalendar back = _sut.GetSettings(_companyId);
            Assert.That(back.ProrationBasis, Is.EqualTo(ProrationBasis.WorkingDays));
            Assert.That(back.WorkFri, Is.True);
            Assert.That(back.RestDayRate, Is.EqualTo(1.5m));
        }

        [Test]
        public void BuildExample_ShowsAllThreeBases_WithDefaultMarked()
        {
            long emp = AddEmployee(new DateTime(2024, 1, 1), null, 30000m);
            ProrationExample ex = _sut.BuildExample(emp, 2026, 7, 3);

            Assert.That(ex.Rows.Count, Is.EqualTo(3));
            Assert.That(ex.MonthDays, Is.EqualTo(31));
            ProrationExampleRow def = ex.Rows.Single(r => r.Basis == ProrationBasis.MonthDays);
            Assert.That(def.IsCurrentDefault, Is.True);
            Assert.That(def.Denominator, Is.EqualTo(31m));
            Assert.That(def.Deduction, Is.EqualTo(2903.23m));
            Assert.That(ex.Rows.Single(r => r.Basis == ProrationBasis.FixedThirtieths).Deduction, Is.EqualTo(3000.00m));
        }
    }
}
