using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Regression guard for the Performance module's on-entry data path: exercises every
    /// service call PerformanceViewModel.OnActivated + the seven hub tabs make, across an
    /// empty company, employees-without-reviews (incl. an empty department), and a fully
    /// launched cycle with completed reviews + a goal. None may throw — the module must
    /// never crash the app when it is opened.
    /// </summary>
    [TestFixture]
    public sealed class PerformanceEntryPathTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private IPerformanceService _perf;
        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "perfcrash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var f = new SqliteConnectionFactory(Path.Combine(_dir, "t.db"));
            using (var c = f.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(f);
            _perf = new PerformanceService(_uow, new AttendanceService(_uow));
            using (IUnitOfWork u = _uow.Create())
            {
                u.BeginTransaction();
                _companyId = u.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                u.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch { }
        }

        private long AddEmp(string last, string dept)
        {
            using (IUnitOfWork u = _uow.Create())
            {
                u.BeginTransaction();
                long id = u.Employees.Insert(new Employee
                {
                    CompanyId = _companyId, LastNameFr = last, FirstNameFr = "T", Department = dept,
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(2020, 1, 1), BaseSalary = 50000m,
                    Poste = "Agent", IsActive = true
                });
                u.Commit();
                return id;
            }
        }

        private void ScoreComplete(long reviewId, decimal score)
        {
            PerformanceReview r = _perf.Get(reviewId);
            List<PerformanceCriterion> crit;
            using (IUnitOfWork u = _uow.Create()) crit = u.Performance.GetCriteria(reviewId).ToList();
            foreach (var c in crit) c.Score = score;
            r.Reviewer = "DRH";
            _perf.Save(r, crit);
            _perf.Complete(reviewId);
        }

        /// <summary>Exactly what PerformanceViewModel.OnActivated + every tab.Refresh() call.</summary>
        private void RunEntryPath(int year)
        {
            _ = _perf.GetByCompanyYear(_companyId, year).ToList();          // Load()
            _ = _perf.GetTemplates(_companyId).ToList();                    // Modeles
            foreach (var cy in _perf.GetCycles(_companyId).ToList())        // Cycles + detail
                _ = _perf.GetCycleDetail(cy.CycleId);
            _ = _perf.GetDashboard(_companyId);                            // Dashboard
            _ = _perf.GetCalibration(_companyId, year);                    // Calibrage
            _ = _perf.GetCompanyGoals(_companyId).ToList();                // Objectifs
            // Comparaison + Parcours just list employees; timeline for each:
            using (IUnitOfWork u = _uow.Create())
                foreach (var e in u.Employees.GetByCompany(_companyId))
                    _ = _perf.GetCareerTimeline(e.Id);
        }

        [Test]
        public void EntryPath_EmptyCompany_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunEntryPath(DateTime.Today.Year));
        }

        [Test]
        public void EntryPath_WithEmployeesNoReviews_DoesNotThrow()
        {
            AddEmp("BENALI", "Commercial");
            AddEmp("TOUATI", "Production");
            AddEmp("ZERO", "");   // empty department — a common real edge case
            Assert.DoesNotThrow(() => RunEntryPath(DateTime.Today.Year));
        }

        [Test]
        public void EntryPath_WithLaunchedCycleAndReviews_DoesNotThrow()
        {
            long a = AddEmp("BENALI", "Commercial");
            long b = AddEmp("TOUATI", "Production");
            long cEmp = AddEmp("AMRANI", "");   // no department

            int year = DateTime.Today.Year;
            Result<long> cyc = _perf.LaunchCycle(new CycleLaunchRequest
            {
                CompanyId = _companyId, Name = "T1", CycleType = PerformanceCycleType.Quarterly,
                StartDate = new DateTime(year, 1, 1), EndDate = new DateTime(year, 3, 31),
                PeriodYear = year, PeriodLabel = "T1"
            });
            Assert.That(cyc.IsSuccess, Is.True, cyc.Error);

            // Complete the reviews the cycle created (varied scores across depts + no-dept employee).
            List<PerformanceSummary> reviews = _perf.GetByCompanyYear(_companyId, year).ToList();
            decimal[] scores = { 18m, 9m, 14m };
            for (int i = 0; i < reviews.Count; i++) ScoreComplete(reviews[i].ReviewId, scores[i % scores.Length]);

            _perf.CreateGoal(new PerformanceGoal { EmployeeId = a, Title = "Vendre", TargetMetric = "120%", ProgressPercent = 50m });

            Assert.DoesNotThrow(() => RunEntryPath(year));
        }
    }
}
