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
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Evaluation rebuild (Part 1) — the department-weighted grid: the KPI 1-5 mapping,
    /// department-grid resolution for a new review, and that saving derives a KPI
    /// criterion's score from its target/achieved and folds it into the weighted overall.
    /// Proves migration 0027 applies and seeds the five department grids.
    /// </summary>
    [TestFixture]
    public sealed class EvaluationCriteriaTests
    {
        private static readonly int Year = DateTime.Today.Year;

        private string _directory;
        private IUnitOfWorkFactory _uowFactory;
        private IPerformanceService _service;
        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-eval-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _uowFactory = new UnitOfWorkFactory(factory);
            _service = new PerformanceService(_uowFactory, new AttendanceService(_uowFactory));

            using (IUnitOfWork uow = _uowFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* WAL still held */ }
        }

        private long AddEmployee(string dept)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                uow.BeginTransaction();
                long id = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId,
                    LastNameFr = "TEST",
                    FirstNameFr = "Agent",
                    Department = dept,
                    Gender = Gender.Male,
                    MaritalStatus = MaritalStatus.Single,
                    PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi,
                    HireDate = new DateTime(2020, 1, 1),
                    BaseSalary = 50000m,
                    Poste = "Agent",
                    IsActive = true
                });
                uow.Commit();
                return id;
            }
        }

        private List<PerformanceCriterion> Criteria(long reviewId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                return uow.Performance.GetCriteria(reviewId).ToList();
            }
        }

        // ---------------------------------------------------------------- KPI 1-5 mapping

        [Test]
        public void Kpi_AchievedAboveTarget_MatchesTheSpecExample()
        {
            // 100 000 achieved vs 90 000 target → ratio 1.11 → ~4.6 / 5
            decimal score = PerformanceService.KpiScoreOnScale(90000m, 100000m, true, 5m);
            Assert.That(score, Is.EqualTo(4.56m).Within(0.05m));
        }

        [Test]
        public void Kpi_ExactlyOnTarget_ScoresFourOnFive()
        {
            Assert.That(PerformanceService.KpiScoreOnScale(1000m, 1000m, true, 5m), Is.EqualTo(4m));
        }

        [Test]
        public void Kpi_FarAboveTarget_ClampsToFive()
        {
            Assert.That(PerformanceService.KpiScoreOnScale(100m, 300m, true, 5m), Is.EqualTo(5m));
        }

        [Test]
        public void Kpi_FarBelowTarget_ClampsToOne()
        {
            Assert.That(PerformanceService.KpiScoreOnScale(1000m, 100m, true, 5m), Is.EqualTo(1m));
        }

        [Test]
        public void Kpi_LowerIsBetter_ZeroAchievedIsBest()
        {
            // e.g. zero defects against a target of 5 → best score
            Assert.That(PerformanceService.KpiScoreOnScale(5m, 0m, false, 5m), Is.EqualTo(5m));
        }

        [Test]
        public void Kpi_ScalesToTheReviewScale()
        {
            // on-target on a /20 review → 16 / 20 (== 4/5)
            Assert.That(PerformanceService.KpiScoreOnScale(1000m, 1000m, true, 20m), Is.EqualTo(16m));
        }

        [Test]
        public void Kpi_MissingValues_AreUnscored()
        {
            Assert.That(PerformanceService.KpiScoreOnScale(null, 100m, true, 5m), Is.EqualTo(0m));
            Assert.That(PerformanceService.KpiScoreOnScale(100m, null, true, 5m), Is.EqualTo(0m));
        }

        // ---------------------------------------------------------------- grid resolution

        [Test]
        public void CreateForEmployee_Commercial_UsesTheCommercialGridWithAKpiCriterion()
        {
            long emp = AddEmployee("Commercial");
            var result = _service.CreateForEmployee(emp, Year, "S1", "DRH", null, null);
            Assert.That(result.IsSuccess, Is.True);

            List<PerformanceCriterion> criteria = Criteria(result.Value);
            Assert.That(criteria.Sum(c => c.Weight), Is.EqualTo(100m));
            Assert.That(criteria.Any(c => c.CriterionType == CriterionType.Kpi), Is.True, "commercial grid has a sales KPI");
        }

        [Test]
        public void CreateForEmployee_Production_UsesTheProductionGrid()
        {
            long emp = AddEmployee("Production");
            var result = _service.CreateForEmployee(emp, Year, "S1", "DRH", null, null);
            List<PerformanceCriterion> criteria = Criteria(result.Value);
            Assert.That(criteria.Any(c => c.Label.StartsWith("Qualité")), Is.True);
            Assert.That(criteria.Any(c => c.CriterionType == CriterionType.Kpi), Is.True);
        }

        [Test]
        public void CreateForEmployee_UnknownDepartment_FallsBackToGenerale()
        {
            long emp = AddEmployee("Marketing digital");
            var result = _service.CreateForEmployee(emp, Year, "S1", "DRH", null, null);
            List<PerformanceCriterion> criteria = Criteria(result.Value);
            Assert.That(criteria.Count, Is.EqualTo(4));
            Assert.That(criteria.All(c => c.CriterionType == CriterionType.Behavioral), Is.True);
        }

        // ---------------------------------------------------------------- save & scoring

        [Test]
        public void Save_DerivesKpiScoreAndFoldsItIntoTheWeightedOverall()
        {
            long emp = AddEmployee("Commercial");
            long id = _service.CreateForEmployee(emp, Year, "S1", "DRH", null, null).Value;

            List<PerformanceCriterion> criteria = Criteria(id);
            foreach (PerformanceCriterion c in criteria)
            {
                if (c.CriterionType == CriterionType.Kpi)
                {
                    c.KpiTarget = 1000000m;
                    c.KpiAchieved = 1200000m; // ratio 1.2 → 5 / 5
                }
                else
                {
                    c.Score = 4m;
                }
            }

            PerformanceReview review = _service.Get(id);
            review.Reviewer = "DRH";
            var save = _service.Save(review, criteria);
            Assert.That(save.IsSuccess, Is.True, save.Error);

            PerformanceReview saved = _service.Get(id);
            // weighted: 5*50 + 4*20 + 4*15 + 4*15 = 450 / 100 = 4.5 (on /5)
            Assert.That(saved.OverallScore, Is.EqualTo(4.5m));

            PerformanceCriterion kpi = Criteria(id).First(c => c.CriterionType == CriterionType.Kpi);
            Assert.That(kpi.Score, Is.EqualTo(5m));
            Assert.That(kpi.KpiTarget, Is.EqualTo(1000000m));
            Assert.That(kpi.KpiAchieved, Is.EqualTo(1200000m));
        }

        // ---------------------------------------------------------------- template duplication

        [Test]
        public void DuplicateTemplate_PreservesTheKpiCriterionType()
        {
            long sourceId = _service.GetTemplates(_companyId).First(t => t.GroupKey == "dept-commercial").TemplateId;

            long copyId = _service.DuplicateTemplate(sourceId, _companyId, "Ma grille commerciale").Value;

            TemplateDetail copy = _service.GetTemplateDetail(copyId);
            Assert.That(copy.Criteria.Any(c => c.CriterionType == CriterionType.Kpi), Is.True,
                "duplicating a grid must keep its KPI criteria numeric, not silently behavioral");
        }

        [Test]
        public void Save_BehavioralCriterion_KeepsItsDirectScore()
        {
            long emp = AddEmployee("Générale");
            long id = _service.CreateForEmployee(emp, Year, "S1", "DRH", null, null).Value;

            List<PerformanceCriterion> criteria = Criteria(id);
            foreach (PerformanceCriterion c in criteria) c.Score = 3m;

            PerformanceReview review = _service.Get(id);
            review.Reviewer = "DRH";
            Assert.That(_service.Save(review, criteria).IsSuccess, Is.True);

            Assert.That(_service.Get(id).OverallScore, Is.EqualTo(3m));
        }
    }
}
