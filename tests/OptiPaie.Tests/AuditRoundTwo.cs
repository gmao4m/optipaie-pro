using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Tests
{
    /// <summary>
    /// E2E audit round 2: D5 real orphan scan (Task 6) and the half-day table facts (Task 3).
    /// Evidence by execution: real seed/mutations + PRAGMA output on the resulting database.
    /// </summary>
    [TestFixture]
    public sealed class AuditRoundTwo
    {
        private string _dir;
        private SqliteConnectionFactory _factory;
        private UnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private ConfigurationService _config;
        private PayrollService _payroll;
        private LoanService _loans;
        private ArchiveService _archive;
        private ContractService _contracts;
        private AttendanceService _attendance;
        private BatchPayrollService _batch;
        private PayslipRevisionService _revision;

        private static readonly int Year = DateTime.Today.Year - 1;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-audit2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(_factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _config = new ConfigurationService(_uow);
            _payroll = new PayrollService(_uow, _config, new PayrollCalculationEngine());
            _loans = new LoanService(_uow);
            _archive = new ArchiveService(_uow);
            _contracts = new ContractService(_uow);
            _attendance = new AttendanceService(_uow);
            _batch = new BatchPayrollService(
                _employees, new PayrollElementService(_uow, new PayrollElementValidator()),
                _loans, _attendance, _payroll, _contracts, _archive, _ => true);
            _revision = new PayslipRevisionService(_uow, _payroll, _config, _loans);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private long NewCompany() =>
            _companies.Create(new Company { NameFr = "SARL Audit", Nif = "000000000000000" }).Value;

        private long NewEmployee(long companyId, int i) =>
            _employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "EMP" + i.ToString("000"), FirstNameFr = "P",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(Year - 1, 1, 1),
                BaseSalary = 40000m + i, IsActive = true
            }).Value;

        // ---- generic FK orphan tooling (the authoritative D5 check) ----

        /// <summary>Every orphan across the whole database in one pass (SQLite's own checker).</summary>
        private int ForeignKeyCheckRows()
        {
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            using (SQLiteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_key_check;";
                using (SQLiteDataReader r = cmd.ExecuteReader()) { int n = 0; while (r.Read()) n++; return n; }
            }
        }

        /// <summary>Per child-table/column orphan counts, the "table | column | count" the audit asks for.</summary>
        private void PrintPerForeignKeyOrphanCounts()
        {
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            {
                var tables = new List<string>();
                using (SQLiteCommand t = c.CreateCommand())
                {
                    t.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                    using (SQLiteDataReader r = t.ExecuteReader()) while (r.Read()) tables.Add(r.GetString(0));
                }

                TestContext.Out.WriteLine("Table | Column | Parent | Orphans");
                foreach (string table in tables)
                {
                    var fks = new List<(string from, string parent, string to)>();
                    using (SQLiteCommand fk = c.CreateCommand())
                    {
                        fk.CommandText = "PRAGMA foreign_key_list(\"" + table + "\");";
                        using (SQLiteDataReader r = fk.ExecuteReader())
                            while (r.Read()) fks.Add((Convert.ToString(r["from"]), Convert.ToString(r["table"]), Convert.ToString(r["to"])));
                    }
                    foreach ((string from, string parent, string to) in fks)
                    {
                        string pk = string.IsNullOrEmpty(to) ? "Id" : to;
                        using (SQLiteCommand q = c.CreateCommand())
                        {
                            q.CommandText = "SELECT COUNT(*) FROM \"" + table + "\" ch LEFT JOIN \"" + parent +
                                "\" pa ON ch.\"" + from + "\" = pa.\"" + pk + "\" WHERE ch.\"" + from +
                                "\" IS NOT NULL AND pa.\"" + pk + "\" IS NULL;";
                            long n = Convert.ToInt64(q.ExecuteScalar(), CultureInfo.InvariantCulture);
                            if (n > 0) TestContext.Out.WriteLine(table + " | " + from + " | " + parent + " | " + n + "  <<< ORPHANS");
                        }
                    }
                }
            }
        }

        // ================================================================= Task 6 — orphans (D5 real)

        [Test]
        public void ForeignKey_Orphans_StayZero_AfterSeed_Scale_Correction_AndDeletion()
        {
            long companyId = NewCompany();

            // (b) scale: 200 employees, payroll generated for several periods.
            var emps = new List<long>();
            for (int i = 1; i <= 200; i++) emps.Add(NewEmployee(companyId, i));
            _batch.Run(companyId, Year, 1);
            _batch.Run(companyId, Year, 2);

            Assert.That(ForeignKeyCheckRows(), Is.EqualTo(0), "(b) 200 employees + 2 periods: no orphans");

            // (a) add + delete: soft-delete some employees (they keep payslips → must not orphan).
            for (int i = 0; i < 10; i++) _employees.Delete(emps[i]);
            Assert.That(ForeignKeyCheckRows(), Is.EqualTo(0), "(a) after employee deletions: no orphans");

            // (c) S20 correct + S21 cancel a saved payslip for month 3.
            long pCorrect = _payroll.Generate(_batch.BuildRequest(companyId, emps[20], Year, 3)).Value;
            Result<long> corrected = _revision.Correct(new PayslipCorrectionRequest
            {
                OriginalPayslipId = pCorrect,
                Amended = _batch.BuildRequest(companyId, emps[20], Year, 3),
                Reason = "audit S20"
            });
            Assert.That(corrected.IsSuccess, Is.True, corrected.Error);

            long pCancel = _payroll.Generate(_batch.BuildRequest(companyId, emps[21], Year, 3)).Value;
            Result cancel = _revision.Cancel(pCancel, "audit S21");
            Assert.That(cancel.IsSuccess, Is.True, cancel.Error);

            PrintPerForeignKeyOrphanCounts();
            Assert.That(ForeignKeyCheckRows(), Is.EqualTo(0), "(c) after correction + cancellation: no orphans");
        }

        // ================================================================= Task 3 — half-day facts

        [Test]
        public void HalfDay_AttendanceSchema_And_WorkedHours_Printed()
        {
            // Q1 — the live table definition, including every CHECK constraint.
            string schema;
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            using (SQLiteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='AttendanceRecords';";
                schema = Convert.ToString(cmd.ExecuteScalar());
            }
            TestContext.Out.WriteLine("=== .schema AttendanceRecords ===");
            TestContext.Out.WriteLine(schema);
            Assert.That(schema, Does.Contain("Status"), "schema must define Status");

            // Q2 — WorkedHours for the existing statuses, from a real write+read.
            long companyId = NewCompany();
            long empId = NewEmployee(companyId, 1);
            var present = new DateTime(Year, 6, 2);
            var absent = new DateTime(Year, 6, 3);
            _attendance.SetDayStatusBulk(new[] { new AttendanceDayStatus(empId, present, AttendanceStatus.Present) });
            _attendance.SetDayStatusBulk(new[] { new AttendanceDayStatus(empId, absent, AttendanceStatus.Absent) });

            AttendanceRecord p = _attendance.Get(empId, present);
            AttendanceRecord a = _attendance.Get(empId, absent);
            TestContext.Out.WriteLine("WorkedHours — Present day = " + p.WorkedHours.ToString(CultureInfo.InvariantCulture));
            TestContext.Out.WriteLine("WorkedHours — Absent day  = " + a.WorkedHours.ToString(CultureInfo.InvariantCulture));
            Assert.That(a.WorkedHours, Is.EqualTo(0m), "an absent day carries no worked hours");
        }
    }
}
