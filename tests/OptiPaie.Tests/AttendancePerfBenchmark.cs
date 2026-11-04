using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// [Explicit] real-number diagnosis of the pointage (attendance) module on a realistic dataset
    /// (150 employees, one month). Measures the matrix LOAD (one query) and the three SAVE paths the
    /// screen uses, to quantify the N+1 / per-transaction cost before any rework. Read-only w.r.t. the
    /// product: it exercises the SHIPPING services exactly as the matrix does. Prints timings.
    /// </summary>
    [TestFixture, Explicit]
    public sealed class AttendancePerfBenchmark
    {
        private const int Employees = 150;
        // A fully-elapsed past month, so every working day counts (a realistic full matrix).
        private static readonly DateTime Ref = DateTime.Today.AddMonths(-2);
        private static readonly int Year = Ref.Year;
        private static readonly int Month = Ref.Month;

        [Test]
        public void Measure_Attendance_Load_And_Save_Paths()
        {
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-att-perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            IUnitOfWorkFactory uow = new UnitOfWorkFactory(factory);

            var companies = new CompanyService(uow, new CompanyValidator());
            var employees = new EmployeeService(uow, new EmployeeValidator());
            var attendance = new AttendanceService(uow);

            long companyId = companies.Create(new Company { NameFr = "SARL Perf", Nif = "000000000000000" }).Value;
            var empIds = new List<long>();
            for (int i = 1; i <= Employees; i++)
            {
                empIds.Add(employees.Create(new Employee
                {
                    CompanyId = companyId, LastNameFr = "EMP" + i.ToString("000"), FirstNameFr = "P",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(Year - 1, 1, 1), BaseSalary = 40000m, IsActive = true
                }).Value);
            }

            int days = DateTime.DaysInMonth(Year, Month);
            var workingDays = Enumerable.Range(1, days)
                .Select(d => new DateTime(Year, Month, d))
                .Where(d => d.Date <= DateTime.Today && d.DayOfWeek != DayOfWeek.Friday && d.DayOfWeek != DayOfWeek.Saturday)
                .ToList();

            // --- SAVE path 1: "apply month to all" (the heaviest bulk the matrix issues) ---
            var allEntries = new List<AttendanceDayStatus>();
            foreach (long id in empIds)
                foreach (DateTime d in workingDays)
                    allEntries.Add(new AttendanceDayStatus(id, d, AttendanceStatus.Present));

            var sw = Stopwatch.StartNew();
            attendance.SetDayStatusBulk(allEntries);
            sw.Stop();
            long bulkInsertMs = sw.ElapsedMilliseconds;

            // Re-apply the SAME (now every entry hits the UPDATE branch after a point-SELECT) ---
            sw.Restart();
            attendance.SetDayStatusBulk(allEntries);
            sw.Stop();
            long bulkUpdateMs = sw.ElapsedMilliseconds;

            // --- LOAD path: the matrix opening (one GetByCompanyRange) ---
            sw.Restart();
            IReadOnlyList<AttendanceRecord> loaded = attendance.GetCompanyMonth(companyId, Year, Month);
            sw.Stop();
            long loadMs = sw.ElapsedMilliseconds;

            // --- SAVE path 2a: erase ONE day for all employees via the OLD per-cell ClearDay ---
            DateTime eraseDay = workingDays.Last();
            sw.Restart();
            foreach (long id in empIds) attendance.ClearDay(id, eraseDay); // N independent transactions (old EraseCells)
            sw.Stop();
            long erasePerCellMs = sw.ElapsedMilliseconds;

            // --- SAVE path 2b: NEW atomic ClearDayBulk for the same day (1 transaction) ---
            attendance.SetDayStatusBulk(empIds.Select(id => new AttendanceDayStatus(id, eraseDay, AttendanceStatus.Present)).ToList());
            sw.Restart();
            attendance.ClearDayBulk(empIds.Select(id => new AttendanceDayStatus(id, eraseDay, AttendanceStatus.Present)).ToList());
            sw.Stop();
            long eraseBulkMs = sw.ElapsedMilliseconds;

            // --- SAVE path 3: single cell paint (one SetDayStatus = its own transaction) ---
            sw.Restart();
            for (int i = 0; i < 50; i++) attendance.SetDayStatus(empIds[i], workingDays[0], AttendanceStatus.Absent);
            sw.Stop();
            long single50Ms = sw.ElapsedMilliseconds;

            int cells = allEntries.Count;
            TestContext.Out.WriteLine("=== Pointage perf (150 employees, " + workingDays.Count + " working days) ===");
            TestContext.Out.WriteLine("Matrix LOAD (GetCompanyMonth, 1 query)          : " + loadMs + " ms for " + loaded.Count + " records");
            TestContext.Out.WriteLine("Bulk 'apply month to all' INSERT (" + cells + " cells): " + bulkInsertMs + " ms  (" + cells + " UPSERTs, 1 tx, no point-SELECT)");
            TestContext.Out.WriteLine("Bulk 'apply month to all' UPDATE (" + cells + " cells): " + bulkUpdateMs + " ms  (" + cells + " UPSERTs update-in-place, 1 tx)");
            TestContext.Out.WriteLine("Erase one day, " + Employees + " cells, OLD per-cell ClearDay: " + erasePerCellMs + " ms  (" + Employees + " SEPARATE transactions)");
            TestContext.Out.WriteLine("Erase one day, " + Employees + " cells, NEW ClearDayBulk      : " + eraseBulkMs + " ms  (1 atomic transaction)");
            TestContext.Out.WriteLine("Paint 50 single cells (50 transactions)          : " + single50Ms + " ms  (" + (single50Ms / 50.0).ToString("0.0") + " ms/click)");

            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
