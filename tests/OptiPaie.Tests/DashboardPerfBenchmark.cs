using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
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
    /// Dashboard load profiler — NOT a pass/fail test (marked [Explicit], never runs in CI).
    /// Seeds a single company with N employees and proportional module data, then measures the
    /// full dashboard data build: SQL statements issued, connections opened, and wall-clock time,
    /// at N = 20 and N = 500. Run before and after the perf rework to get real before/after numbers.
    ///   dotnet test --filter "FullyQualifiedName~DashboardPerfBenchmark" -c Release
    /// </summary>
    [TestFixture, Explicit]
    public sealed class DashboardPerfBenchmark
    {
        private string _dir;
        private IUnitOfWorkFactory _uowf;
        private SqliteConnectionFactory _factory;

        private CompanyService _companies;
        private EmployeeService _employees;
        private ContractService _contracts;
        private LeaveService _leave;
        private LoanService _loans;
        private AttendanceService _attendance;
        private AtsService _ats;
        private AssetService _assets;
        private TrainingService _training;
        private DashboardService _dashboard;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uowf = new UnitOfWorkFactory(_factory);

            _companies = new CompanyService(_uowf, new CompanyValidator());
            _employees = new EmployeeService(_uowf, new EmployeeValidator());
            _contracts = new ContractService(_uowf);
            _leave = new LeaveService(_uowf);
            _loans = new LoanService(_uowf);
            _attendance = new AttendanceService(_uowf);
            _ats = new AtsService(_uowf, new EmployeeValidator());
            _assets = new AssetService(_uowf);
            _training = new TrainingService(_uowf);
            _dashboard = new DashboardService(_companies, _employees, _contracts, _leave, _loans, _attendance, _ats, _assets, _training);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [TestCase(20)]
        [TestCase(500)]
        public void Profile_DashboardBuild(int n)
        {
            long companyId = Seed(n);

            // Warm up (JIT + page cache) so the measured run reflects steady state, not first-touch.
            RunFullBuild(companyId);

            SqliteConnectionFactory.Profile = true;
            SqliteConnectionFactory.ResetProfile();
            var sw = Stopwatch.StartNew();
            RunFullBuild(companyId);
            sw.Stop();
            SqliteConnectionFactory.Profile = false;

            TestContext.WriteLine("──────────────────────────────────────────────");
            TestContext.WriteLine($"  Dashboard build @ {n} employees");
            TestContext.WriteLine($"    SQL statements executed : {SqliteConnectionFactory.StatementsExecuted}");
            TestContext.WriteLine($"    Connections opened      : {SqliteConnectionFactory.ConnectionsOpened}");
            TestContext.WriteLine($"    Wall-clock              : {sw.Elapsed.TotalMilliseconds:0.0} ms");
            TestContext.WriteLine("──────────────────────────────────────────────");
        }

        // Toggle: measure the OLD multi-call path (Build + 2 employee loads + BuildWorkforce) or the
        // NEW single consolidated call (BuildOverview). Both produce the full dashboard dataset.
        public static bool UseConsolidated = true;

        private void RunFullBuild(long companyId)
        {
            var firstOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            if (UseConsolidated)
            {
                _dashboard.BuildOverview(companyId, firstOfMonth, DateTime.Today, 30);
            }
            else
            {
                _dashboard.Build(companyId, 30);
                _employees.GetByCompany(companyId, false);
                _employees.GetByCompany(companyId, true);
                _dashboard.BuildWorkforce(companyId, firstOfMonth, DateTime.Today);
            }
        }

        private long Seed(int n)
        {
            long companyId = _companies.Create(new Company { NameFr = "SARL Perf", Nif = "000000000000000" }).Value;

            var depts = new[] { "Production", "Ventes", "Administration", "Logistique", "Finance", "RH" };
            var postes = new[] { "Chef", "Ouvrier", "Cadre", "Technicien", "Agent", "Commercial" };
            var cats = new[] { "Cadre", "Maîtrise", "Exécution" };
            var rng = new Random(12345);

            for (int i = 0; i < n; i++)
            {
                using (IUnitOfWork uow = _uowf.Create())
                {
                    uow.BeginTransaction();
                    long empId = uow.Employees.Insert(new Employee
                    {
                        CompanyId = companyId,
                        LastNameFr = "NOM" + i,
                        FirstNameFr = "Prenom" + i,
                        Gender = (i % 2 == 0) ? Gender.Male : Gender.Female,
                        BirthDate = (i % 7 == 0) ? (DateTime?)null : DateTime.Today.AddYears(-22 - (i % 40)),
                        Category = cats[i % cats.Length],
                        ContractType = (i % 3 == 0) ? ContractType.Cdd : ContractType.Cdi,
                        MaritalStatus = (i % 2 == 0) ? MaritalStatus.Married : MaritalStatus.Single,
                        Department = depts[i % depts.Length],
                        Poste = postes[i % postes.Length],
                        HireDate = DateTime.Today.AddDays(-(i * 11 % 3000)),
                        ExitDate = (i % 25 == 0) ? (DateTime?)DateTime.Today.AddDays(-(i % 20)) : null,
                        PaymentMode = PaymentMode.Cash,
                        BaseSalary = 40000m + (i % 20) * 1500m,
                        IsActive = (i % 25 != 0)
                    });
                    uow.Commit();

                    // Proportional module data — this is what makes the N+1 loops do real work.
                    if (i % 5 == 0) // loans (N/5) — N+1 on repayments today
                    {
                        _loans.Save(new Loan
                        {
                            EmployeeId = empId, Type = LoanType.Loan, Principal = 100000m,
                            MonthlyInstallment = 10000m, StartYear = DateTime.Today.Year, StartMonth = DateTime.Today.Month
                        });
                    }
                    if (i % 2 == 0) // assets (N/2) — double N+1 (open assignment + holder name)
                    {
                        long asset = _assets.Save(new Asset
                        {
                            CompanyId = companyId, Name = "Bien" + i, Category = AssetCategory.Laptop, PurchaseValue = 80000m
                        }).Value;
                        if (i % 4 == 0) _assets.Assign(asset, empId, DateTime.Today, "Neuf", null);
                    }
                }
            }

            // ~10 postings, each with several candidates — N+1 on candidate count per posting.
            for (int p = 0; p < 10; p++)
            {
                long posting = _ats.SavePosting(new JobPosting
                {
                    CompanyId = companyId, Title = "Poste" + p, Department = depts[p % depts.Length], OpenDate = DateTime.Today, Positions = 2
                }).Value;
                for (int c = 0; c < 4; c++)
                    _ats.SaveCandidate(new Candidate { PostingId = posting, LastName = "C" + p + c, FirstName = "X", Phone = "0555000" + p + c });
            }

            // ~15 training sessions — N+1 on participant count per session.
            for (int t = 0; t < 15; t++)
                _training.Save(new TrainingSession { CompanyId = companyId, Title = "Form" + t, StartDate = DateTime.Today.AddDays(7 + t) });

            return companyId;
        }
    }
}
