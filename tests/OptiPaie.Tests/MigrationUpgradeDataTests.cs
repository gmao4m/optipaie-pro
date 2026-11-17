using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using OptiPaie.Data.Migrations;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The upgrade proven on DATA, not by reading the script. A database is frozen at an earlier
    /// (published) schema, filled with real attendance rows, loans, a payroll run, a payslip and
    /// repayments, then stepped forward through EVERY remaining migration — including the heaviest
    /// one in this release, the AttendanceRecords table rebuild (0041), and the loan→payslip link
    /// with its historical back-fill (0042). The test asserts every row survives with the same
    /// values: the attendance table byte-for-byte, the repayments intact and correctly linked.
    /// </summary>
    [TestFixture]
    public sealed class MigrationUpgradeDataTests
    {
        private const int PublishedSchema = 40; // last version before the attendance rebuild (0041) + loan link (0042)

        private string _dir;
        private string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dbPath = Path.Combine(_dir, "published.db");
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private SQLiteConnection Open()
        {
            var c = new SQLiteConnection("Data Source=" + _dbPath + ";Version=3;");
            c.Open();
            using (SQLiteCommand p = c.CreateCommand()) { p.CommandText = "PRAGMA foreign_keys=OFF;"; p.ExecuteNonQuery(); }
            return c;
        }

        private static void Exec(SQLiteConnection c, string sql)
        {
            using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }

        private static string HashQuery(SQLiteConnection c, string sql)
        {
            var sb = new StringBuilder();
            using (SQLiteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = sql;
                using (SQLiteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        for (int i = 0; i < r.FieldCount; i++)
                        {
                            sb.Append(r.IsDBNull(i) ? "<null>" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture));
                            sb.Append('\u001f');
                        }
                        sb.Append('\u001e');
                    }
                }
            }

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }

        private static object Scalar(SQLiteConnection c, string sql)
        {
            using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); }
        }

        [Test]
        public void Upgrade_From_Published_Schema_Preserves_Every_Row()
        {
            const string attendanceSelect =
                "SELECT Id, EmployeeId, WorkDate, Status, CheckIn, CheckOut, WorkedHours, LateMinutes, " +
                "OvertimeHours, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted, RowVersion " +
                "FROM AttendanceRecords ORDER BY Id;";

            string attendanceBefore;

            using (SQLiteConnection c = Open())
            {
                // Freeze at the published schema.
                int applied = new MigrationRunner(c).Run(PublishedSchema);
                Assert.That(applied, Is.GreaterThan(0), "migrations through the published schema ran");
                Assert.That(Scalar(c, "SELECT COUNT(*) FROM AttendanceRecords;"), Is.Not.Null, "AttendanceRecords exists at the published schema");

                // Real attendance rows — the full pre-rebuild status range (1..7), decimals, notes, RowVersion.
                Exec(c,
                    "INSERT INTO AttendanceRecords (Id, EmployeeId, WorkDate, Status, CheckIn, CheckOut, WorkedHours, LateMinutes, OvertimeHours, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted, RowVersion) VALUES " +
                    "(1, 10, '2024-03-01', 1, '08:00', '16:30', '8.5', 0,  '0',   NULL,       '2024-03-01 09:00:00', NULL, 0, 3)," +
                    "(2, 10, '2024-03-02', 2, NULL,    NULL,    '0',   0,  '0',   'absent',   '2024-03-02 09:00:00', '2024-03-02 10:00:00', 0, 1)," +
                    "(3, 10, '2024-03-03', 4, NULL,    NULL,    '0',   0,  '0',   'congé',    '2024-03-03 09:00:00', NULL, 0, 0)," +
                    "(4, 10, '2024-03-04', 6, NULL,    NULL,    '0',   15, '1.25','repos',    '2024-03-04 09:00:00', NULL, 0, 7)," +
                    "(5, 11, '2024-03-01', 7, '07:45', '17:00', '9.0', 0,  '0.5', 'mission à Oran', '2024-03-01 09:00:00', NULL, 0, 2);");

                // A loan, a payroll run, an Active payslip and three repayments (pre-0042: no PayslipId column yet).
                Exec(c, "INSERT INTO Loans (Id, EmployeeId, Type, Status, Principal, MonthlyInstallment, StartYear, StartMonth, Reason, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                        "VALUES (1, 10, 1, 1, '150000', '15000', 2024, 3, 'avance', NULL, '2024-03-01 09:00:00', NULL, 0);");
                Exec(c, "INSERT INTO PayrollRuns (Id, CompanyId, PeriodYear, PeriodMonth, RunStatus, GeneratedAtUtc, EngineVersion, CreatedAtUtc) " +
                        "VALUES (1, 1, 2024, 3, 1, '2024-03-31 10:00:00', 'test', '2024-03-31 10:00:00');");
                Exec(c, "INSERT INTO Payslips (Id, RunId, EmployeeId, GeneratedAtUtc, CreatedAtUtc, Status) " +
                        "VALUES (1, 1, 10, '2024-03-31 10:00:00', '2024-03-31 10:00:00', 1);");
                Exec(c, "INSERT INTO LoanRepayments (Id, LoanId, Year, Month, Amount, IsManual, CreatedAtUtc, IsDeleted) VALUES " +
                        "(1, 1, 2024, 3, '15000', 0, '2024-03-31 10:00:00', 0)," +  // payroll → should link to payslip 1
                        "(2, 1, 2024, 4, '5000',  1, '2024-04-05 10:00:00', 0)," +  // manual   → stays NULL
                        "(3, 1, 2024, 5, '15000', 0, '2024-05-31 10:00:00', 0);");  // payroll, no payslip on file → stays NULL

                attendanceBefore = HashQuery(c, attendanceSelect);
            }

            // Step the SAME database forward through every remaining migration (0041 rebuild + 0042 link + any later).
            using (SQLiteConnection c = Open())
            {
                int applied = new MigrationRunner(c).Run();
                Assert.That(applied, Is.GreaterThan(0), "the rebuild + link migrations were pending and ran");
            }

            using (SQLiteConnection c = Open())
            {
                // Attendance: every row survives the table rebuild byte-for-byte.
                Assert.That(Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM AttendanceRecords;")), Is.EqualTo(5), "no attendance row lost in the rebuild");
                Assert.That(HashQuery(c, attendanceSelect), Is.EqualTo(attendanceBefore),
                    "the attendance table is identical to the centime after the 0041 rebuild");

                // Loan repayments: all three survive; the payroll one is linked, the manual and the orphan stay NULL.
                Assert.That(Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM LoanRepayments;")), Is.EqualTo(3));
                Assert.That(Convert.ToInt64(Scalar(c, "SELECT PayslipId FROM LoanRepayments WHERE Id = 1;")), Is.EqualTo(1L),
                    "the payroll repayment is back-filled to the issued payslip");
                Assert.That(Scalar(c, "SELECT PayslipId FROM LoanRepayments WHERE Id = 2;"), Is.EqualTo(DBNull.Value),
                    "a manual repayment is never linked");
                Assert.That(Scalar(c, "SELECT PayslipId FROM LoanRepayments WHERE Id = 3;"), Is.EqualTo(DBNull.Value),
                    "a payroll repayment with no payslip on file stays unlinked, not lost");

                // The amounts and periods of the repayments are untouched.
                Assert.That(Convert.ToString(Scalar(c, "SELECT Amount FROM LoanRepayments WHERE Id = 1;")), Is.EqualTo("15000"));
                Assert.That(Convert.ToInt32(Scalar(c, "SELECT Month FROM LoanRepayments WHERE Id = 3;")), Is.EqualTo(5));
            }
        }
    }
}
