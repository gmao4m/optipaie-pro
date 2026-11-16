using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 3 — day counters are decimal so half days work. The ONE risk is a decimal truncating
    /// to int (2.5 → 2), silently over-paying forever. Every test here is aimed at that: each asserts
    /// the EXACT fractional amount, so reintroducing a truncation anywhere on the path would fail it.
    /// </summary>
    [TestFixture]
    public sealed class DecimalDayCounterTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private WorkCalendarService _sut;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-dec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
            _sut = new WorkCalendarService(_uow, new HolidayService(_uow, AccessContext.Unrestricted), AccessContext.Unrestricted);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void Deduction_For_Half_Days_Is_Exact_Not_Truncated()
        {
            // base 31000 in a 31-day month: ÷31 per day = 1000.00/day exactly.
            Assert.That(_sut.Deduction(31000m, 0.5m, 31m), Is.EqualTo(500.00m), "0.5 day = 500, not 0 and not 1000");
            Assert.That(_sut.Deduction(31000m, 1.5m, 31m), Is.EqualTo(1500.00m));
            Assert.That(_sut.Deduction(31000m, 2.5m, 31m), Is.EqualTo(2500.00m), "2.5 days = 2500, NOT 2000 (trunc→2) and NOT 3000 (→3)");
            // a month of several half days summing to a non-integer total (five halves = 2.5) in a 30-day month.
            Assert.That(_sut.Deduction(31000m, 2.5m, 30m), Is.EqualTo(2583.33m)); // 31000×2.5/30 = 2583.333… → 2583.33
        }

        [Test]
        public void Engine_Prorates_Fractional_WorkedDays_Without_Truncation()
        {
            // 2.5 absent days in July (31) → WorkedDays = 28.5. base 31000 × 28.5/31 = 28500.00 exactly.
            // A truncation to 28 would give 28000, to 29 would give 29000 — the test pins 28500.
            var money = new PayrollContext
            {
                Period = new PayrollPeriod(2026, 7),
                BaseSalary = 31000m,
                WorkedDays = 28.5m,
                WorkableDays = 31m,
                WorkedHours = 0m,
                Legal = new LegalSnapshot(0.09m, 0.26m, 24000m, RoundingPolicy.Centime),
                Elements = new List<PayrollElementInput>()
            };

            PayrollResult result = new PayrollCalculationEngine().Calculate(money);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Totals.SalaireBrut, Is.EqualTo(28500m), "fractional worked days must prorate exactly");
        }

        [Test]
        public void Summary_Carries_Half_Days_Through_PaidDays()
        {
            var s = new AttendanceSummary { Year = 2026, Month = 7, RecordedDays = 1m, AbsentDays = 2.5m };
            Assert.That(s.PaidDays, Is.EqualTo(28.5m), "31 − 2.5 = 28.5, not 28 or 29");

            // several half days summing to a fraction
            s.AbsentDays = 0.5m + 0.5m + 0.5m; // 1.5
            Assert.That(s.AbsentDays, Is.EqualTo(1.5m));
            Assert.That(s.PaidDays, Is.EqualTo(29.5m));
        }

        [Test]
        public void Whole_Values_RoundTrip_Unchanged()
        {
            var s = new AttendanceSummary { Year = 2026, Month = 6, RecordedDays = 22m, AbsentDays = 3m };
            Assert.That(s.AbsentDays, Is.EqualTo(3m), "a whole value stays exact, not 3.0000001");
            Assert.That(s.PaidDays, Is.EqualTo(27m)); // 30 − 3
            Assert.That(_sut.Deduction(30000m, 3m, 30m), Is.EqualTo(3000.00m), "whole-day deduction unchanged");
        }
    }
}
