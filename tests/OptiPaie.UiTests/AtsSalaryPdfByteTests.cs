using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Certificates;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// COMMIT H — the ATS (Attestation de Travail et de Salaire) is proven on the BYTES. Its page-2
    /// table carries, per month, the persisted contributable base (the same salary figure the CNAS
    /// declaration uses), sourced ONLY through the archive. This renders the REAL ATS overlay PDF onto
    /// the pre-printed CNAS form and extracts its text with PdfPig: when produced by a role without
    /// ViewSalary the salary is ABSENT (the archive returns nothing, the rows are struck); with a
    /// payroll role it is PRESENT (positive control).
    /// </summary>
    [TestFixture, Apartment(System.Threading.ApartmentState.STA)]
    public sealed class AtsSalaryPdfByteTests
    {
        private const decimal Sentinel = 918273.64m;

        private string _dir;
        private SqliteConnectionFactory _factory;
        private UnitOfWorkFactory _uow;
        private long _companyId;
        private long _employeeId;

        [OneTimeSetUp]
        public void StageFormAssets()
        {
            // The ATS overlays onto the pre-printed CNAS form; stage the real form assets next to the
            // test assembly so the renderer finds them (they ship under the Desktop project's Assets).
            string src = Path.Combine(RepoRoot(), "src", "OptiPaie.Desktop", "Assets", "AtsDrtTemplates");
            string dst = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AtsDrtTemplates");
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        }

        [SetUp]
        public void SetUp()
        {
            SqliteTypeHandlers.Register();
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-ats-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "s.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(_factory);

            using (SQLiteConnection c = _factory.CreateOpenConnection())
            {
                Exec(c, "INSERT INTO Companies (NameFr, City, CnasEmployerNumber, CreatedAtUtc) " +
                        "VALUES ('SARL Test', 'Alger', '12345678901234', '2024-01-01 00:00:00');");
                _companyId = Scalar(c, "SELECT last_insert_rowid();");
                Exec(c, "INSERT INTO Employees (CompanyId, LastNameFr, FirstNameFr, Nss, BirthDate, HireDate, BaseSalary, CreatedAtUtc) " +
                        "VALUES (" + _companyId + ", 'Benali', 'Karim', '8512030012345', '1985-12-03', '2020-01-01', '" + Inv(Sentinel) + "', '2024-01-01 00:00:00');");
                _employeeId = Scalar(c, "SELECT last_insert_rowid();");
                Exec(c, "INSERT INTO PayrollRuns (CompanyId, PeriodYear, PeriodMonth, RunStatus, EngineVersion, CreatedAtUtc) " +
                        "VALUES (" + _companyId + ", 2024, 6, 2, '1.0', '2024-06-30 00:00:00');");
                long runId = Scalar(c, "SELECT last_insert_rowid();");
                Exec(c, "INSERT INTO Payslips (RunId, EmployeeId, BaseCotisable, NetSalaire, WorkedDays, GeneratedAtUtc, CreatedAtUtc) " +
                        "VALUES (" + runId + ", " + _employeeId + ", '" + Inv(Sentinel) + "', '500000', '26', '2024-06-30 00:00:00', '2024-06-30 00:00:00');");
            }
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void AtsPdf_Bytes_CarriesNoSalary_WithoutViewSalary()
        {
            Assert.That(SalaryPdfByteTests.HasSalary(SalaryPdfByteTests.PdfText(AtsPdf(UserRole.ReadOnly))), Is.False,
                "ATS PDF (bytes): no contributable-base salary without ViewSalary (archive returns nothing)");
            Assert.That(SalaryPdfByteTests.HasSalary(SalaryPdfByteTests.PdfText(AtsPdf(UserRole.Payroll))), Is.True,
                "positive control: the payroll role's ATS carries the contributable base in its bytes");
        }

        private byte[] AtsPdf(UserRole role)
        {
            var companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            var employees = new EmployeeService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            var archive = new ArchiveService(_uow, new Ctx(role));           // the salary source is gated here
            var svc = new AtsDrtDocumentService(companies, employees, archive);

            Company company = svc.MapCompany(_companyId);
            Employee employee = svc.MapEmployee(_employeeId);
            List<MonthlyContribution> contributions =
                svc.BuildContributionsFromPayroll(_companyId, _employeeId, new DateTime(2024, 6, 1), 12, false);

            var stoppage = new WorkStoppage { StoppageDate = new DateTime(2024, 6, 10), NumberOfDays = 3 };
            string path = Path.Combine(_dir, "ats-" + role + ".pdf");
            svc.GenerateAts(company, employee, stoppage, hasResumedWork: true,
                contributions, new WeekendConfig(), 0, 0, path);
            return File.ReadAllBytes(path);
        }

        private static string Inv(decimal v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static void Exec(SQLiteConnection c, string sql)
        {
            using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }

        private static long Scalar(SQLiteConnection c, string sql)
        {
            using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar()); }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln"))) dir = dir.Parent;
            return dir?.FullName ?? TestContext.CurrentContext.TestDirectory;
        }

        private sealed class Ctx : IAccessContext
        {
            private readonly UserRole _role;
            public Ctx(UserRole role) { _role = role; }
            public bool IsEnforced => true;
            public UserRole CurrentRole => _role;
            public bool IsOwner => false;
            public long? CurrentUserId => null;
        }
    }
}
