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

        // ---------------------------------------------------------------- DAC (tranche 2a)

        [Test]
        public void BuildDac_throws_when_no_company_is_specified()
        {
            Assert.Throws<ArgumentException>(() => _service.BuildDac(0, Year, new[] { Month }));
        }

        [Test]
        public void BuildDac_aggregates_period_assiette_effectif_and_applied_cotisations()
        {
            long companyId = AddCompany("SARL DAC", "1234567890");
            long empId = AddEmployee(companyId, "BENALI", "012345678901", new DateTime(1985, 1, 1), 60000m);
            _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, Month));

            CnasDacReport dac = _service.BuildDac(companyId, Year, new[] { Month });

            Assert.That(dac.Effectif, Is.EqualTo(1));
            Assert.That(dac.Assiette, Is.EqualTo(60000m));
            // Applied = configured rates (9% / 26%). Frozen = the payslip's own contributions.
            Assert.That(dac.CotisationSalariale, Is.EqualTo(5400m));
            Assert.That(dac.CotisationPatronale, Is.EqualTo(15600m));
            Assert.That(dac.FrozenCnasEmployee, Is.EqualTo(5400m));
            Assert.That(dac.FrozenCnasEmployer, Is.EqualTo(15600m));
        }

        [Test]
        public void BuildDac_exposes_the_official_split_and_the_gap()
        {
            long companyId = AddCompany("SARL DAC", "1234567890");
            long empId = AddEmployee(companyId, "BENALI", "012345678901", new DateTime(1985, 1, 1), 60000m);
            _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, Month));

            CnasDacReport dac = _service.BuildDac(companyId, Year, new[] { Month });

            // Official split (décret 94-187): patronale 25,5 %, salariale 9 % — display only.
            Assert.That(dac.OfficialBranches.Sum(b => b.PatronaleRate), Is.EqualTo(0.255m));
            Assert.That(dac.OfficialBranches.Sum(b => b.SalarialeRate), Is.EqualTo(0.09m));
            Assert.That(dac.OfficialCotisationPatronale, Is.EqualTo(15300m)); // 60000 * 0,255
            // The gap to arbitrate: 26 % (applied) vs 25,5 % (official) = 0,5 point = 300 DA.
            Assert.That(dac.EcartPatronaleDa, Is.EqualTo(300m));
            Assert.That(dac.EcartPatronalePoints, Is.EqualTo(0.5m));
            Assert.That(dac.HasRateGap, Is.True);
        }

        [Test]
        public void BuildDac_sums_a_quarter_over_three_months()
        {
            long companyId = AddCompany("SARL DAC", "1234567890");
            long empId = AddEmployee(companyId, "BENALI", "012345678901", new DateTime(1985, 1, 1), 60000m);
            foreach (int m in new[] { 4, 5, 6 })
            {
                _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, m));
            }

            CnasDacReport dac = _service.BuildDac(companyId, Year, new[] { 4, 5, 6 });

            Assert.That(dac.Effectif, Is.EqualTo(1));
            Assert.That(dac.Assiette, Is.EqualTo(180000m)); // 3 × 60000
        }

        [Test]
        public void BuildDac_is_strictly_company_scoped()
        {
            long companyA = AddCompany("SARL A", "1111111111");
            long companyB = AddCompany("SARL B", "2222222222");
            long empA = AddEmployee(companyA, "ALPHA", "012345678901", new DateTime(1990, 1, 1), 50000m);
            long empB = AddEmployee(companyB, "BETA", "012345678902", new DateTime(1990, 1, 1), 90000m);
            _payroll.Generate(_batch.BuildRequest(companyA, empA, Year, Month));
            _payroll.Generate(_batch.BuildRequest(companyB, empB, Year, Month));

            CnasDacReport dac = _service.BuildDac(companyA, Year, new[] { Month });

            Assert.That(dac.Effectif, Is.EqualTo(1));
            Assert.That(dac.Assiette, Is.EqualTo(50000m)); // only company A, never B's 90000
        }

        // -- Rounding robustness (non-round, decimal salaries) -------------------------------

        // Deliberately jagged assiette so no figure is a "clean" multiple of any rate.
        private static readonly decimal[] JaggedSalaries =
            { 47316.42m, 33908.77m, 61245.19m, 52104.63m, 28750.05m, 71980.11m, 39412.88m };

        [Test]
        public void BuildDac_reconciles_branches_exactly_and_only_display_rounding_leaves_a_bounded_residue()
        {
            long companyId = AddCompany("SARL Arrondi", "1234567890");
            int n = 0;
            foreach (decimal s in JaggedSalaries)
            {
                long id = AddEmployee(companyId, "SAL" + n, "01234567890" + n, new DateTime(1985, 1, 1), s);
                _payroll.Generate(_batch.BuildRequest(companyId, id, Year, Month));
                n++;
            }

            CnasDacReport dac = _service.BuildDac(companyId, Year, new[] { Month });

            // The 7 jagged salaries have no allowances, so each BaseCotisable = base salary.
            Assert.That(dac.Assiette, Is.EqualTo(334718.05m));

            // (1) + (2) — FULL PRECISION: the 6 patronal / 4 salarial branch amounts reconcile
            // EXACTLY to the official totals. The split introduces NO systematic error.
            Assert.That(dac.OfficialBranches.Sum(b => b.PatronaleAmount), Is.EqualTo(dac.OfficialCotisationPatronale));
            Assert.That(dac.OfficialBranches.Sum(b => b.SalarialeAmount), Is.EqualTo(dac.OfficialCotisationSalariale));

            // The ONLY discrepancy is at the 2-decimal DISPLAY: the sum of the rounded branch
            // amounts can differ from the rounded total by a centime or two. We measure it,
            // prove it is present for these salaries, and bound it — never absorb it silently.
            decimal sumPat = dac.OfficialBranches.Sum(b => Math.Round(b.PatronaleAmount, 2, MidpointRounding.AwayFromZero));
            decimal sumSal = dac.OfficialBranches.Sum(b => Math.Round(b.SalarialeAmount, 2, MidpointRounding.AwayFromZero));
            decimal residuePat = Math.Round(dac.OfficialCotisationPatronale, 2, MidpointRounding.AwayFromZero) - sumPat;
            decimal residueSal = Math.Round(dac.OfficialCotisationSalariale, 2, MidpointRounding.AwayFromZero) - sumSal;
            TestContext.WriteLine($"assiette={dac.Assiette}  résidu patronal={residuePat}  résidu salarial={residueSal}");

            Assert.That(residuePat, Is.EqualTo(-0.02m)); // sum of rounded branches 85 353,12 vs total 85 353,10
            Assert.That(residueSal, Is.EqualTo(-0.01m));
            // Bounded by construction: ≤ half a centime per rounded term.
            Assert.That(Math.Abs(residuePat), Is.LessThanOrEqualTo(0.04m));
            Assert.That(Math.Abs(residueSal), Is.LessThanOrEqualTo(0.03m));
        }

        [Test]
        public void BuildDac_frozen_withholding_can_diverge_from_the_aggregate_rederivation()
        {
            // The DAC re-derives the cotisation from the AGGREGATE assiette: Round2(Σbase × rate).
            // Payroll instead withheld Round2(base_i × rate) on EACH payslip and froze it. The sum
            // of those per-slip amounts (FrozenCnasEmployee) can differ from the aggregate figure —
            // and that gap grows with headcount, unlike the bounded ≤0,02 display residues. This is a
            // real withheld-vs-declared reconciliation, not a display artefact.
            long companyId = AddCompany("SARL Frozen", "1234567890");
            int n = 0;
            foreach (decimal s in JaggedSalaries)
            {
                long id = AddEmployee(companyId, "SAL" + n, "01234567890" + n, new DateTime(1985, 1, 1), s);
                _payroll.Generate(_batch.BuildRequest(companyId, id, Year, Month));
                n++;
            }

            CnasDacReport dac = _service.BuildDac(companyId, Year, new[] { Month });
            decimal aggregateSalariale = Math.Round(dac.CotisationSalariale, 2, MidpointRounding.AwayFromZero);

            Assert.That(aggregateSalariale, Is.EqualTo(30124.62m));   // Round2(334718,05 × 9%)
            Assert.That(dac.FrozenCnasEmployee, Is.EqualTo(30124.63m)); // Σ per-payslip Round2(base × 9%)
            Assert.That(dac.FrozenCnasEmployee - aggregateSalariale, Is.EqualTo(0.01m)); // 7 salariés → 1 centime
        }

        [Test]
        public void BuildDac_quarter_equals_the_sum_of_its_three_months_with_decimal_salaries()
        {
            long companyId = AddCompany("SARL Trim Décimal", "1234567890");
            int n = 0;
            foreach (decimal s in JaggedSalaries)
            {
                long id = AddEmployee(companyId, "SAL" + n, "01234567890" + n, new DateTime(1985, 1, 1), s);
                foreach (int m in new[] { 4, 5, 6 }) _payroll.Generate(_batch.BuildRequest(companyId, id, Year, m));
                n++;
            }

            CnasDacReport t2 = _service.BuildDac(companyId, Year, new[] { 4, 5, 6 });
            CnasDacReport m4 = _service.BuildDac(companyId, Year, new[] { 4 });
            CnasDacReport m5 = _service.BuildDac(companyId, Year, new[] { 5 });
            CnasDacReport m6 = _service.BuildDac(companyId, Year, new[] { 6 });

            // (3) — the quarter equals the exact sum of its three months, to the centime.
            Assert.That(m4.Assiette + m5.Assiette + m6.Assiette, Is.EqualTo(t2.Assiette));
            Assert.That(t2.Assiette, Is.EqualTo(334718.05m * 3));
            Assert.That(m4.CotisationPatronale + m5.CotisationPatronale + m6.CotisationPatronale, Is.EqualTo(t2.CotisationPatronale));
            Assert.That(m4.CotisationSalariale + m5.CotisationSalariale + m6.CotisationSalariale, Is.EqualTo(t2.CotisationSalariale));
            Assert.That(m4.OfficialCotisationPatronale + m5.OfficialCotisationPatronale + m6.OfficialCotisationPatronale,
                Is.EqualTo(t2.OfficialCotisationPatronale));
        }
    }
}
