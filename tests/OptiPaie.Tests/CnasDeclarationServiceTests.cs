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
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// CNAS declarations readiness (tranche 1) — read-only. Proves the check is strictly
    /// company-scoped (never null), flags missing/invalid identity data, and flags a payslip
    /// month whose cotisable base is below the SNMG. On real SQLite, real services.
    /// </summary>
    [TestFixture]
    public sealed class CnasDeclarationServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uow;

        private ICompanyService _companies;
        private IEmployeeService _employees;
        private IContractService _contracts;
        private IArchiveService _archive;
        private IConfigurationService _config;
        private IPayrollService _payroll;
        private IBatchPayrollService _batch;
        private ICnasDeclarationService _service;

        private static readonly int Year = DateTime.Today.Year;
        private const int Month = 6;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-cnas-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _contracts = new ContractService(_uow);
            _archive = new ArchiveService(_uow);
            _config = new ConfigurationService(_uow);
            _payroll = new PayrollService(_uow, _config, new PayrollCalculationEngine());
            _batch = new BatchPayrollService(
                _employees, new PayrollElementService(_uow, new PayrollElementValidator()),
                new LoanService(_uow), new AttendanceService(_uow), _payroll, _contracts, _archive, _ => true);
            _service = new CnasDeclarationService(_companies, _employees, _archive, _config);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        private long AddCompany(string name, string cnasNumber) =>
            _companies.Create(new Company { NameFr = name, Nif = "000000000000000", CnasEmployerNumber = cnasNumber }).Value;

        private long AddEmployee(long companyId, string last, string nss, DateTime? birth, decimal salary)
        {
            long id = _employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = last, FirstNameFr = "Karim", Poste = "Agent",
                Nss = nss, BirthDate = birth, Gender = Gender.Male, MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash, ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year - 2, 1, 1), BaseSalary = salary, IsActive = true
            }).Value;
            long contractId = _contracts.CreateDraftFromEmployee(id).Value;
            _contracts.Activate(contractId);
            return id;
        }

        [Test]
        public void CheckReadiness_throws_when_no_company_is_specified()
        {
            Assert.Throws<ArgumentException>(() => _service.CheckReadiness(0, Year));
            Assert.Throws<ArgumentException>(() => _service.CheckReadiness(-1, Year));
        }

        [Test]
        public void CheckReadiness_flags_missing_identity_and_employer_number()
        {
            long companyId = AddCompany("SARL Sans Numéro", null); // no CNAS employer number
            long empId = AddEmployee(companyId, "SANSNSS", null, null, 60000m); // no NSS, no birth date

            CnasReadinessReport report = _service.CheckReadiness(companyId, Year);

            Assert.That(report.EmployerNumberMissing, Is.True);
            CnasEmployeeReadiness row = report.Employees.Single(e => e.EmployeeId == empId);
            Assert.That(row.NssMissing, Is.True);
            Assert.That(row.BirthDateMissing, Is.True);
            Assert.That(row.IsReady, Is.False);
            Assert.That(report.IsReady, Is.False);
        }

        [Test]
        public void CheckReadiness_flags_malformed_nss_and_employer_number()
        {
            long companyId = AddCompany("SARL Mauvais Num", "12-34"); // not 10 digits
            long empId = AddEmployee(companyId, "MAUVAISNSS", "123", new DateTime(1990, 1, 1), 60000m); // NSS not 12 digits

            CnasReadinessReport report = _service.CheckReadiness(companyId, Year);

            Assert.That(report.EmployerNumberMalformed, Is.True);
            Assert.That(report.Employees.Single(e => e.EmployeeId == empId).NssMalformed, Is.True);
        }

        [Test]
        public void CheckReadiness_marks_complete_employee_as_ready()
        {
            long companyId = AddCompany("SARL Complète", "1234567890"); // 10 digits
            AddEmployee(companyId, "COMPLET", "012345678901", new DateTime(1988, 5, 3), 60000m); // 12-digit NSS + birth date

            CnasReadinessReport report = _service.CheckReadiness(companyId, Year);

            Assert.That(report.IsReady, Is.True);
            Assert.That(report.ReadyCount, Is.EqualTo(1));
            Assert.That(report.EmployeesWithIssues, Is.Empty);
        }

        [Test]
        public void CheckReadiness_flags_a_payslip_month_below_snmg()
        {
            long companyId = AddCompany("SARL Bas Salaire", "1234567890");
            long empId = AddEmployee(companyId, "BASSALAIRE", "012345678901", new DateTime(1990, 1, 1), 20000m); // < SNMG 24000

            _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, Month));

            CnasReadinessReport report = _service.CheckReadiness(companyId, Year);
            CnasEmployeeReadiness row = report.Employees.Single(e => e.EmployeeId == empId);

            Assert.That(row.PayslipMonths, Is.EqualTo(1));
            Assert.That(row.MonthsBelowSnmg, Is.EqualTo(1));
            Assert.That(row.IsReady, Is.False);
        }

        [Test]
        public void CheckReadiness_is_strictly_company_scoped()
        {
            long companyA = AddCompany("SARL A", "1111111111");
            long companyB = AddCompany("SARL B", "2222222222");
            long empA = AddEmployee(companyA, "ALPHA", "012345678901", new DateTime(1990, 1, 1), 60000m);
            long empB = AddEmployee(companyB, "BETA", "012345678902", new DateTime(1990, 1, 1), 60000m);

            // A payslip below SNMG in company B must never surface in company A's report.
            _payroll.Generate(_batch.BuildRequest(companyB, empB, Year, Month));

            CnasReadinessReport reportA = _service.CheckReadiness(companyA, Year);

            Assert.That(reportA.Employees.Select(e => e.EmployeeId), Does.Contain(empA));
            Assert.That(reportA.Employees.Select(e => e.EmployeeId), Does.Not.Contain(empB));
            Assert.That(reportA.CnasEmployerNumber, Is.EqualTo("1111111111"));
        }
    }
}
