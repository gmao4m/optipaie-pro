using System;
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
    /// HR dashboard — workforce (effectif) analytics. One indicator per assertion, against a real
    /// SQLite database with a controlled roster. Isolation, the turnover formula, the case-insensitive
    /// free-text grouping and the « non renseigné » bucket are all pinned here.
    /// </summary>
    [TestFixture]
    public sealed class WorkforceAnalyticsTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uowf;
        private DashboardService _dashboard;
        private long _companyId, _otherCompanyId;

        private readonly DateTime _today = DateTime.Today;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-wf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uowf = new UnitOfWorkFactory(factory);

            var employees = new EmployeeService(_uowf, new EmployeeValidator());
            _dashboard = new DashboardService(
                new CompanyService(_uowf, new CompanyValidator()), employees, new ContractService(_uowf),
                new LeaveService(_uowf), new LoanService(_uowf), new AttendanceService(_uowf),
                new AtsService(_uowf, new EmployeeValidator()), new AssetService(_uowf), new TrainingService(_uowf));

            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL A", Nif = "000000000000000" });
                _otherCompanyId = uow.Companies.Insert(new Company { NameFr = "SARL B", Nif = "111111111111111" });

                // Active roster of SARL A.
                Emp(uow, _companyId, Gender.Male, _today.AddYears(-30), "Cadre", ContractType.Cdi, MaritalStatus.Married, "Production", "Chef", _today.AddYears(-6), null, true);
                Emp(uow, _companyId, Gender.Female, _today.AddYears(-40), "cadre", ContractType.Cdd, MaritalStatus.Single, "production", "Ouvrier", _today.AddYears(-2), null, true); // casing differs on purpose
                Emp(uow, _companyId, Gender.Male, null, "", ContractType.Cdi, MaritalStatus.Single, "", "Ouvrier", _today.AddDays(-15), null, true); // unknown age/category/dept ; hired in period
                // A leaver — exited within the period.
                Emp(uow, _companyId, Gender.Female, _today.AddYears(-50), "Maîtrise", ContractType.Cdd, MaritalStatus.Divorced, "Ventes", "Commercial", _today.AddYears(-3), _today.AddDays(-10), false);
                // Another company's employee must never leak in.
                Emp(uow, _otherCompanyId, Gender.Male, _today.AddYears(-33), "Cadre", ContractType.Cdi, MaritalStatus.Married, "Autre", "Autre", _today.AddYears(-1), null, true);
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static void Emp(IUnitOfWork uow, long companyId, Gender g, DateTime? birth, string category,
            ContractType ct, MaritalStatus ms, string dept, string poste, DateTime hire, DateTime? exit, bool active)
        {
            uow.Employees.Insert(new Employee
            {
                CompanyId = companyId, LastNameFr = "N", FirstNameFr = "P", Gender = g, BirthDate = birth,
                Category = category, ContractType = ct, MaritalStatus = ms, Department = dept, Poste = poste,
                HireDate = hire, ExitDate = exit, PaymentMode = PaymentMode.Cash, BaseSalary = 40000m, IsActive = active
            });
        }

        private WorkforceAnalytics BuildLastMonth() =>
            _dashboard.BuildWorkforce(_companyId, _today.AddMonths(-1), _today);

        [Test]
        public void Headcount_CountsActiveEmployeesOfTheCompanyOnly()
        {
            WorkforceAnalytics w = BuildLastMonth();
            Assert.That(w.Headcount, Is.EqualTo(3), "3 actifs (le sortant et l'employé d'une autre société sont exclus)");
            Assert.That(w.HeadcountIds.Count, Is.EqualTo(3));
        }

        [Test]
        public void AverageAge_IsComputedOnlyOnKnownBirthDates_AndAnnotated()
        {
            WorkforceAnalytics w = BuildLastMonth();
            Assert.That(w.AgeTotalCount, Is.EqualTo(3));
            Assert.That(w.AgeKnownCount, Is.EqualTo(2), "un actif n'a pas de date de naissance");
            Assert.That(w.AverageAge, Is.EqualTo(35m), "(30 + 40) / 2");
        }

        [Test]
        public void Turnover_UsesDeparturesOverAverageHeadcount_WithRawComponents()
        {
            WorkforceAnalytics w = BuildLastMonth();
            Assert.That(w.Exits, Is.EqualTo(1), "un départ dans la période");
            Assert.That(w.Entries, Is.EqualTo(1), "une entrée dans la période");
            Assert.That(w.HeadcountStart, Is.EqualTo(3));
            Assert.That(w.HeadcountEnd, Is.EqualTo(3));
            Assert.That(w.AverageHeadcount, Is.EqualTo(3m));
            Assert.That(w.TurnoverRate, Is.EqualTo(33.3m), "1 / 3 × 100, arrondi");
        }

        [Test]
        public void ByGender_And_ByContract_SplitTheActiveEmployees()
        {
            WorkforceAnalytics w = BuildLastMonth();
            Assert.That(w.ByGender.Buckets.Single(b => b.LabelKey == "Enum_Gender_Male").Count, Is.EqualTo(2));
            Assert.That(w.ByGender.Buckets.Single(b => b.LabelKey == "Enum_Gender_Female").Count, Is.EqualTo(1));
            Assert.That(w.ByContract.Buckets.Single(b => b.LabelKey == "Enum_ContractType_Cdi").Count, Is.EqualTo(2));
            Assert.That(w.ByContract.Buckets.Single(b => b.LabelKey == "Enum_ContractType_Cdd").Count, Is.EqualTo(1));
        }

        [Test]
        public void ByCategory_GroupsCaseInsensitively_AndBucketsBlanksAsUnknown()
        {
            WorkforceAnalytics w = BuildLastMonth();
            WorkforceBucket cadre = w.ByCategory.Buckets.Single(b => !b.IsUnknown);
            Assert.That(cadre.Count, Is.EqualTo(2), "« Cadre » et « cadre » comptent ensemble");
            Assert.That(cadre.EmployeeIds.Count, Is.EqualTo(2));
            WorkforceBucket unknown = w.ByCategory.Buckets.Single(b => b.IsUnknown);
            Assert.That(unknown.Count, Is.EqualTo(1));
            Assert.That(unknown.LabelKey, Is.EqualTo("Workforce_Unknown"));
        }

        [Test]
        public void AgeBands_AreOrderedYoungToOld_AndBlanksAreUnknown()
        {
            WorkforceAnalytics w = BuildLastMonth();
            Assert.That(w.ByAgeBand.Buckets.Single(b => b.LabelKey == "Workforce_Age_2534").Count, Is.EqualTo(1));
            Assert.That(w.ByAgeBand.Buckets.Single(b => b.LabelKey == "Workforce_Age_3544").Count, Is.EqualTo(1));
            Assert.That(w.ByAgeBand.Buckets.Any(b => b.IsUnknown), Is.True, "l'actif sans date de naissance");
            // Natural order: the young band index precedes the older one.
            var keys = w.ByAgeBand.Buckets.Where(b => !b.IsUnknown).Select(b => b.LabelKey).ToList();
            Assert.That(keys.IndexOf("Workforce_Age_2534"), Is.LessThan(keys.IndexOf("Workforce_Age_3544")));
        }

        [Test]
        public void EveryBucket_CarriesTheEmployeeIds_ForDrillDown()
        {
            WorkforceAnalytics w = BuildLastMonth();
            foreach (WorkforceBucket b in w.ByDepartment.Buckets)
                Assert.That(b.EmployeeIds.Count, Is.EqualTo(b.Count), "chaque tranche porte la liste nominative exacte");
        }

        [Test]
        public void Unreliable_IsFlagged_WhenMoreThanHalfAreUnknown()
        {
            // Company A ByCategory: 1 unknown of 3 -> reliable.
            Assert.That(BuildLastMonth().ByCategory.Unreliable, Is.False);

            // A company where the majority have no category.
            long c;
            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                c = uow.Companies.Insert(new Company { NameFr = "SARL C", Nif = "222222222222222" });
                Emp(uow, c, Gender.Male, _today.AddYears(-30), "", ContractType.Cdi, MaritalStatus.Single, "D", "P", _today.AddYears(-1), null, true);
                Emp(uow, c, Gender.Male, _today.AddYears(-31), "", ContractType.Cdi, MaritalStatus.Single, "D", "P", _today.AddYears(-1), null, true);
                Emp(uow, c, Gender.Male, _today.AddYears(-32), "Cadre", ContractType.Cdi, MaritalStatus.Single, "D", "P", _today.AddYears(-1), null, true);
                uow.Commit();
            }
            Assert.That(_dashboard.BuildWorkforce(c, _today.AddMonths(-1), _today).ByCategory.Unreliable, Is.True,
                "2 non renseignés sur 3 → répartition non fiable");
        }

        [Test]
        public void BuildWorkforce_RequiresACompany()
        {
            Assert.That(() => _dashboard.BuildWorkforce(0, _today.AddMonths(-1), _today),
                Throws.TypeOf<ArgumentOutOfRangeException>(), "jamais « toutes sociétés »");
        }
    }
}
