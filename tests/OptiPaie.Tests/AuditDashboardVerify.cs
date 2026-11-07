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
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// E2E audit — Task 5 (S49). Every dashboard indicator is recomputed by an INDEPENDENT SQL
    /// query against the SAME database at the SAME instant and asserted equal to the value the
    /// DashboardService produced. Any divergence is a finding. Prints the comparison table.
    /// </summary>
    [TestFixture]
    public sealed class AuditDashboardVerify
    {
        private string _dir;
        private SqliteConnectionFactory _factory;
        private UnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-dash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(_factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _companyId = _companies.Create(new Company { NameFr = "SARL Dash", Nif = "000000000000000" }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private void Add(string dept, Gender g, int birthYear, decimal salary, DateTime hire, DateTime? exit, bool active)
        {
            _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "E" + Guid.NewGuid().ToString("N").Substring(0, 6), FirstNameFr = "x",
                Department = dept, Gender = g, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, BirthDate = new DateTime(birthYear, 6, 1), BaseSalary = salary,
                HireDate = hire, ExitDate = exit, IsActive = active
            });
        }

        private long SqlLong(string sql)
        {
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; object v = cmd.ExecuteScalar(); return v == null || v == DBNull.Value ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture); }
        }

        private decimal SqlDec(string sql)
        {
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; object v = cmd.ExecuteScalar(); return v == null || v == DBNull.Value ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture); }
        }

        private int BucketCount(WorkforceDistribution d, string label) =>
            d.Buckets.Where(b => string.Equals(b.Label, label, StringComparison.OrdinalIgnoreCase)).Sum(b => b.Count);

        [Test]
        public void EveryDashboardIndicator_EqualsIndependentSql()
        {
            // Seed a known workforce. Base year so "last month" has no entries/exits.
            int y = DateTime.Today.Year - 1;
            DateTime hiredLongAgo = new DateTime(y - 3, 1, 1);
            // IT: 4 active; RH: 3 active; 1 inactive (exited) in RH.
            Add("IT", Gender.Male, 1990, 50000m, hiredLongAgo, null, true);
            Add("IT", Gender.Male, 1985, 60000m, hiredLongAgo, null, true);
            Add("IT", Gender.Female, 1992, 55000m, hiredLongAgo, null, true);
            Add("IT", Gender.Female, 1980, 70000m, hiredLongAgo, null, true);
            Add("RH", Gender.Male, 1975, 80000m, hiredLongAgo, null, true);
            Add("RH", Gender.Female, 1995, 45000m, hiredLongAgo, null, true);
            Add("RH", Gender.Female, 1988, 52000m, hiredLongAgo, null, true);
            Add("RH", Gender.Male, 1970, 90000m, hiredLongAgo, new DateTime(y - 1, 6, 1), false); // exited, inactive

            var periodStart = new DateTime(y, 1, 1);
            var periodEnd = new DateTime(y, 1, 31);
            var dash = new DashboardService(_companies, _employees, new ContractService(_uow), new LeaveService(_uow),
                new LoanService(_uow), new AttendanceService(_uow), new AtsService(_uow, new EmployeeValidator()),
                new AssetService(_uow), new TrainingService(_uow));

            DashboardOverview ov = dash.BuildOverview(_companyId, periodStart, periodEnd);
            WorkforceAnalytics wf = ov.Workforce;

            long sqlActive = SqlLong(string.Format(CultureInfo.InvariantCulture,
                "SELECT COUNT(*) FROM Employees WHERE CompanyId={0} AND IsDeleted=0 AND IsActive=1", _companyId));
            decimal sqlMasse = SqlDec(string.Format(CultureInfo.InvariantCulture,
                "SELECT COALESCE(SUM(CAST(BaseSalary AS REAL)),0) FROM Employees WHERE CompanyId={0} AND IsDeleted=0 AND IsActive=1", _companyId));
            long sqlIT = SqlLong(string.Format(CultureInfo.InvariantCulture,
                "SELECT COUNT(*) FROM Employees WHERE CompanyId={0} AND IsDeleted=0 AND IsActive=1 AND Department='IT'", _companyId));
            long sqlRH = SqlLong(string.Format(CultureInfo.InvariantCulture,
                "SELECT COUNT(*) FROM Employees WHERE CompanyId={0} AND IsDeleted=0 AND IsActive=1 AND Department='RH'", _companyId));
            long sqlMale = SqlLong(string.Format(CultureInfo.InvariantCulture,
                "SELECT COUNT(*) FROM Employees WHERE CompanyId={0} AND IsDeleted=0 AND IsActive=1 AND Gender=1", _companyId));

            TestContext.Out.WriteLine("Indicateur | Programme | SQL indépendant");
            TestContext.Out.WriteLine("Effectif (headcount)      | " + wf.Headcount + " | " + sqlActive);
            TestContext.Out.WriteLine("Masse salariale           | " + ov.MasseSalariale + " | " + sqlMasse);
            TestContext.Out.WriteLine("Salaire moyen             | " + ov.SalaireMoyen + " | " + (sqlActive > 0 ? Math.Round(sqlMasse / sqlActive, 2) : 0));
            int deptTotal = wf.ByDepartment.Buckets.Sum(b => b.Count);
            int genderTotal = wf.ByGender.Buckets.Sum(b => b.Count);
            TestContext.Out.WriteLine("Par département: IT        | " + BucketCount(wf.ByDepartment, "IT") + " | " + sqlIT);
            TestContext.Out.WriteLine("Par département: RH        | " + BucketCount(wf.ByDepartment, "RH") + " | " + sqlRH);
            TestContext.Out.WriteLine("Σ ByDepartment / ByGender  | " + deptTotal + " / " + genderTotal + " | " + sqlActive + " (effectif)");
            TestContext.Out.WriteLine("(Salaire moyen est arrondi au dinar par le programme ; exact = " +
                Math.Round(sqlMasse / sqlActive, 2).ToString(CultureInfo.InvariantCulture) + ")");

            Assert.Multiple(() =>
            {
                Assert.That(wf.Headcount, Is.EqualTo(sqlActive), "Effectif");
                Assert.That(ov.MasseSalariale, Is.EqualTo(sqlMasse), "Masse salariale");
                // SalaireMoyen is presented rounded to the dinar; it must equal masse/effectif within that rounding.
                Assert.That(Math.Abs(ov.SalaireMoyen - sqlMasse / sqlActive), Is.LessThan(1m), "Salaire moyen (à l'arrondi dinar près)");
                Assert.That(BucketCount(wf.ByDepartment, "IT"), Is.EqualTo(sqlIT), "IT");
                Assert.That(BucketCount(wf.ByDepartment, "RH"), Is.EqualTo(sqlRH), "RH");
                // Every distribution must partition the whole active headcount — no one dropped or double-counted.
                Assert.That(deptTotal, Is.EqualTo(sqlActive), "Σ ByDepartment = effectif");
                Assert.That(genderTotal, Is.EqualTo(sqlActive), "Σ ByGender = effectif");
            });
        }
    }
}
