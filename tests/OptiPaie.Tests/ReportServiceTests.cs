using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The Reports Center. Seeds real data and asserts each report builds a well-formed
    /// uniform table (right columns, rows shaped correctly) — through the real services,
    /// on real SQLite.
    /// </summary>
    [TestFixture]
    public sealed class ReportServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uow;
        private IReportService _reports;

        private ICompanyService _companies;
        private IEmployeeService _employees;
        private IAttendanceService _attendance;
        private ILeaveService _leave;
        private ILoanService _loans;
        private IAtsService _ats;
        private IAssetService _assets;
        private ITrainingService _training;

        private long _companyId;
        private long _employeeId;
        private static readonly int Year = DateTime.Today.Year;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _attendance = new AttendanceService(_uow);
            _leave = new LeaveService(_uow);
            _loans = new LoanService(_uow);
            _ats = new AtsService(_uow);
            _assets = new AssetService(_uow);
            _training = new TrainingService(_uow);
            _reports = new ReportService(_companies, _employees, _attendance, _leave, _loans, _ats, _assets, _training);

            _companyId = _companies.Create(new Company { NameFr = "SARL Test", Nif = "000000000000000" }).Value;
            _employeeId = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "BENALI", FirstNameFr = "Karim", Department = "Production",
                Poste = "Opérateur", Gender = Gender.Male, MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash, ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year, 2, 1), BaseSalary = 60000m, IsActive = true
            }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        [Test]
        public void GetReports_ListsTheLibrary()
        {
            var reports = _reports.GetReports();
            Assert.That(reports.Count, Is.EqualTo(8));
            Assert.That(reports.Any(r => r.Key == ReportService.Headcount));
            Assert.That(reports.Any(r => r.Key == ReportService.Attendance && r.NeedsMonth));
        }

        [Test]
        public void Headcount_ListsActiveEmployees()
        {
            ReportTable t = _reports.Build(ReportService.Headcount, _companyId, Year, 1);

            Assert.That(t.Columns.Count, Is.EqualTo(7));
            Assert.That(t.Rows.Count, Is.EqualTo(1));
            Assert.That(t.Rows[0][1], Is.EqualTo("BENALI Karim"));
            Assert.That(t.Rows[0][2], Is.EqualTo("Production"));
            Assert.That(t.Rows.All(r => r.Count == t.Columns.Count), Is.True, "every row matches the column count");
        }

        [Test]
        public void Turnover_CountsHiresInTheYear_WithATotalRow()
        {
            ReportTable t = _reports.Build(ReportService.Turnover, _companyId, Year, 1);

            Assert.That(t.Columns, Is.EqualTo(new[] { "Mois", "Entrées", "Sorties", "Solde" }));
            Assert.That(t.Rows.Count, Is.EqualTo(13), "12 months + a TOTAL row");
            Assert.That(t.Rows[1][1], Is.EqualTo("1"), "February has one hire");
            Assert.That(t.Rows[12][0], Is.EqualTo("TOTAL"));
            Assert.That(t.Rows[12][1], Is.EqualTo("1"));
        }

        [Test]
        public void Attendance_SummarisesTheMonth()
        {
            _attendance.SetDayStatus(_employeeId, new DateTime(Year, 3, 3), AttendanceStatus.Present);
            _attendance.SetDayStatus(_employeeId, new DateTime(Year, 3, 4), AttendanceStatus.Absent);

            ReportTable t = _reports.Build(ReportService.Attendance, _companyId, Year, 3);

            Assert.That(t.Rows.Count, Is.EqualTo(1));
            Assert.That(t.Rows[0][0], Is.EqualTo("BENALI Karim"));
            Assert.That(t.Rows[0][1], Is.EqualTo("1"), "1 present");
            Assert.That(t.Rows[0][2], Is.EqualTo("1"), "1 absent");
        }

        [Test]
        public void Loans_ShowsOutstandingAndStatus()
        {
            _loans.Save(new Loan
            {
                EmployeeId = _employeeId, Type = LoanType.Loan, Principal = 100000m,
                MonthlyInstallment = 20000m, StartYear = Year, StartMonth = 1
            });

            ReportTable t = _reports.Build(ReportService.Loans, _companyId, Year, 1);

            Assert.That(t.Rows.Count, Is.EqualTo(1));
            Assert.That(t.Rows[0][4], Is.EqualTo("En cours"));
            Assert.That(t.Subtitle, Does.Contain("100"));
        }

        [Test]
        public void Assets_And_Recruitment_Build()
        {
            long asset = _assets.Save(new Asset
            {
                CompanyId = _companyId, Name = "PC", Category = AssetCategory.Laptop, PurchaseValue = 90000m
            }).Value;
            _assets.Assign(asset, _employeeId, DateTime.Today, null, null);

            long posting = _ats.SavePosting(new JobPosting { CompanyId = _companyId, Title = "Magasinier", OpenDate = DateTime.Today, Positions = 1 }).Value;
            _ats.SaveCandidate(new Candidate { PostingId = posting, LastName = "ZIANE" });

            ReportTable assets = _reports.Build(ReportService.Assets, _companyId, Year, 1);
            Assert.That(assets.Rows[0][5], Is.EqualTo("Attribué"));
            Assert.That(assets.Rows[0][4], Is.EqualTo("BENALI Karim"), "holder resolved from the shared record");

            ReportTable rec = _reports.Build(ReportService.Recruitment, _companyId, Year, 1);
            Assert.That(rec.Rows[0][3], Is.EqualTo("1"), "one candidate");
        }

        [Test]
        public void UnknownReport_ReturnsAnEmptyTable_NotACrash()
        {
            ReportTable t = _reports.Build("nope", _companyId, Year, 1);
            Assert.That(t, Is.Not.Null);
            Assert.That(t.Rows.Count, Is.EqualTo(0));
        }
    }
}
