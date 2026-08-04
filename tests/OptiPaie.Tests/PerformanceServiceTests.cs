using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Performance (Évaluation) module — integration tests against a real SQLite file. They
    /// prove the fairness engine: every criterion normalises to 0-100 whatever its type, simple
    /// vs weighted totals, KPI achievement, the five bands, plus templates, periods, evaluations,
    /// the behaviour log, reports and reminders.
    /// </summary>
    [TestFixture]
    public sealed class PerformanceServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uowf;
        private PerformanceService _service;
        private long _companyId, _benali, _touati, _saadi;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uowf = new UnitOfWorkFactory(factory);
            _service = new PerformanceService(_uowf);

            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _benali = Emp(uow, "BENALI", "Karim", "Production");
                _touati = Emp(uow, "TOUATI", "Lila", "Commercial");
                _saadi = Emp(uow, "SAADI", "Farid", "Production");
                uow.Commit();
            }
        }

        private long Emp(IUnitOfWork uow, string last, string first, string dept) =>
            uow.Employees.Insert(new Employee
            {
                CompanyId = _companyId, LastNameFr = last, FirstNameFr = first, Department = dept, Poste = "Poste",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2020, 1, 1), BaseSalary = 60000m, IsActive = true
            });

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        // ---- helpers --------------------------------------------------------

        private static EvalCriterion Crit(string n, CriterionCategory c, ScoreType st, decimal w, decimal? kpi = null) =>
            new EvalCriterion { Name = n, Category = c, ScoreType = st, WeightPercent = w, KpiTarget = kpi };

        private long StarsTemplate(WeightingMode mode = WeightingMode.Simple, bool isDefault = true)
        {
            var crits = new List<EvalCriterion>
            {
                Crit("Qualité", CriterionCategory.Behavioral, ScoreType.Stars5, 50m),
                Crit("Assiduité", CriterionCategory.Behavioral, ScoreType.Stars5, 50m)
            };
            var t = new EvalTemplate { CompanyId = _companyId, Name = "Grille", Department = "Production", WeightingMode = mode, IsDefault = isDefault };
            Result<long> r = _service.SaveTemplate(t, crits);
            Assert.That(r.IsSuccess, Is.True, r.Error);
            return r.Value;
        }

        private long NewPeriod(PeriodStatus status = PeriodStatus.Open)
        {
            var p = new EvalPeriod { CompanyId = _companyId, Name = "Janvier", Cadence = PeriodCadence.Monthly, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 1, 31) };
            long id = _service.SavePeriod(p).Value;
            if (status == PeriodStatus.Closed) _service.ClosePeriod(id);
            return id;
        }

        /// <summary>Scores every rating line to the given stars value and every KPI to full, then completes.</summary>
        private long DoEvaluation(long periodId, long employeeId, decimal stars)
        {
            long evalId = _service.CreateEvaluation(periodId, employeeId, null).Value;
            EvaluationDetail d = _service.GetEvaluationDetail(evalId);
            var scores = d.Scores.ToList();
            foreach (EvaluationScore s in scores)
            {
                if (s.Category == CriterionCategory.Kpi) s.KpiActual = s.KpiTarget;
                else s.RawValue = stars;
                s.NormalizedScore = _service.ComputeLineScore(s);
            }
            _service.SaveEvaluation(d.Evaluation, scores);
            _service.CompleteEvaluation(evalId);
            return evalId;
        }

        // ---- scoring --------------------------------------------------------

        [Test]
        public void ComputeLineScore_NormalisesEachScoreTypeTo0To100()
        {
            Assert.That(_service.ComputeLineScore(new EvaluationScore { Category = CriterionCategory.Behavioral, ScoreType = ScoreType.Stars5, RawValue = 4m }), Is.EqualTo(80m));
            Assert.That(_service.ComputeLineScore(new EvaluationScore { Category = CriterionCategory.Behavioral, ScoreType = ScoreType.Score20, RawValue = 15m }), Is.EqualTo(75m));
            Assert.That(_service.ComputeLineScore(new EvaluationScore { Category = CriterionCategory.Behavioral, ScoreType = ScoreType.Percent, RawValue = 90m }), Is.EqualTo(90m));
        }

        [Test]
        public void ComputeLineScore_Kpi_IsAchievementCappedAt100()
        {
            Assert.That(_service.ComputeLineScore(new EvaluationScore { Category = CriterionCategory.Kpi, KpiTarget = 100m, KpiActual = 90m, HigherIsBetter = true }), Is.EqualTo(90m));
            Assert.That(_service.ComputeLineScore(new EvaluationScore { Category = CriterionCategory.Kpi, KpiTarget = 100m, KpiActual = 130m, HigherIsBetter = true }), Is.EqualTo(100m), "over-achievement caps at 100");
            Assert.That(_service.ComputeLineScore(new EvaluationScore { Category = CriterionCategory.Kpi, KpiTarget = 10m, KpiActual = 20m, HigherIsBetter = false }), Is.EqualTo(50m), "lower-is-better: target/achieved");
        }

        [Test]
        public void ComputeTotal_SimpleIsAverage_WeightedIsWeighted()
        {
            var scores = new List<EvaluationScore>
            {
                new EvaluationScore { NormalizedScore = 80m, WeightPercent = 75m },
                new EvaluationScore { NormalizedScore = 60m, WeightPercent = 25m }
            };
            Assert.That(_service.ComputeTotal(scores, WeightingMode.Simple), Is.EqualTo(70m));
            Assert.That(_service.ComputeTotal(scores, WeightingMode.Weighted), Is.EqualTo(75m));
        }

        [TestCase(92, ClassificationBand.Excellent)]
        [TestCase(80, ClassificationBand.VeryGood)]
        [TestCase(65, ClassificationBand.Good)]
        [TestCase(50, ClassificationBand.Average)]
        [TestCase(30, ClassificationBand.Weak)]
        public void Classify_MapsToBands(decimal score, ClassificationBand expected)
        {
            Assert.That(_service.Classify(score), Is.EqualTo(expected));
        }

        // ---- templates ------------------------------------------------------

        [Test]
        public void SaveTemplate_Weighted_RejectsWeightsNotSummingTo100()
        {
            var t = new EvalTemplate { CompanyId = _companyId, Name = "X", WeightingMode = WeightingMode.Weighted };
            Result<long> r = _service.SaveTemplate(t, new[] { Crit("a", CriterionCategory.Behavioral, ScoreType.Stars5, 30m), Crit("b", CriterionCategory.Behavioral, ScoreType.Stars5, 30m) });
            Assert.That(r.IsFailure, Is.True);
            Assert.That(r.ErrorCode, Is.EqualTo("Performance_TemplateWeightSum"));
        }

        [Test]
        public void SaveTemplate_Simple_IgnoresWeightSum()
        {
            var t = new EvalTemplate { CompanyId = _companyId, Name = "X", WeightingMode = WeightingMode.Simple };
            Result<long> r = _service.SaveTemplate(t, new[] { Crit("a", CriterionCategory.Behavioral, ScoreType.Stars5, 10m) });
            Assert.That(r.IsSuccess, Is.True, r.Error);
        }

        [Test]
        public void GetTemplates_IncludesBuiltInSeed_WhichIsReadOnly()
        {
            var templates = _service.GetTemplates(_companyId);
            TemplateSummary builtIn = templates.FirstOrDefault(x => x.IsBuiltIn);
            Assert.That(builtIn, Is.Not.Null, "the seeded built-in template is visible");
            Result r = _service.DeleteTemplate(builtIn.TemplateId);
            Assert.That(r.IsFailure, Is.True);
            Assert.That(r.ErrorCode, Is.EqualTo("Performance_TemplateBuiltInReadOnly"));
        }

        [Test]
        public void SetDefaultTemplate_ClearsTheOthers()
        {
            long a = StarsTemplate(isDefault: true);
            long b = StarsTemplate(isDefault: true);
            _service.SetDefaultTemplate(_companyId, b);
            var templates = _service.GetTemplates(_companyId);
            Assert.That(templates.Count(t => t.IsDefault), Is.EqualTo(1), "exactly one default");
            Assert.That(templates.First(t => t.TemplateId == b).IsDefault, Is.True);
        }

        // ---- periods --------------------------------------------------------

        [Test]
        public void SavePeriod_RejectsEndBeforeStart()
        {
            var p = new EvalPeriod { CompanyId = _companyId, Name = "P", StartDate = new DateTime(2026, 2, 1), EndDate = new DateTime(2026, 1, 1) };
            Assert.That(_service.SavePeriod(p).IsFailure, Is.True);
        }

        [Test]
        public void ClosePeriod_SetsStatusClosed()
        {
            long id = NewPeriod();
            _service.ClosePeriod(id);
            Assert.That(_service.GetPeriod(id).Status, Is.EqualTo(PeriodStatus.Closed));
        }

        // ---- evaluations ----------------------------------------------------

        [Test]
        public void CreateEvaluation_SnapshotsTemplateCriteriaAsScoreLines()
        {
            StarsTemplate();
            long period = NewPeriod();
            long evalId = _service.CreateEvaluation(period, _benali, null).Value;
            EvaluationDetail d = _service.GetEvaluationDetail(evalId);
            Assert.That(d.Scores.Count, Is.EqualTo(2), "the two template criteria are snapshotted");
            Assert.That(d.Scores.All(s => s.RawValue == null), Is.True, "unscored to start");
        }

        [Test]
        public void CreateEvaluation_IsIdempotentPerEmployeeAndPeriod()
        {
            StarsTemplate();
            long period = NewPeriod();
            long first = _service.CreateEvaluation(period, _benali, null).Value;
            long second = _service.CreateEvaluation(period, _benali, null).Value;
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void SaveAndComplete_ComputesTotalAndSurfacesInHistory()
        {
            StarsTemplate();
            long period = NewPeriod();
            DoEvaluation(period, _benali, 4m); // 4/5 = 80 on both lines → total 80

            var history = _service.GetByEmployee(_benali);
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].TotalScore, Is.EqualTo(80m));
            Assert.That(history[0].Band, Is.EqualTo(ClassificationBand.VeryGood));
            Assert.That(history[0].Status, Is.EqualTo(EvaluationStatus.Done));
        }

        [Test]
        public void GetEvaluationBoard_HasOneRowPerActiveEmployee()
        {
            StarsTemplate();
            long period = NewPeriod();
            DoEvaluation(period, _benali, 5m);
            var board = _service.GetEvaluationBoard(period);
            Assert.That(board.Count, Is.EqualTo(3), "one row per active employee");
            Assert.That(board.Count(r => r.Status == EvaluationStatus.Done), Is.EqualTo(1));
            Assert.That(board.Count(r => r.Status == EvaluationStatus.Pending), Is.EqualTo(2));
        }

        // ---- behaviour log --------------------------------------------------

        [Test]
        public void LogBehavior_IsListedForTheEmployeeAndCompany()
        {
            _service.LogBehavior(_companyId, _benali, true, "a terminé avant l'échéance", DateTime.Today);
            _service.LogBehavior(_companyId, _benali, false, "retard", DateTime.Today);
            var forEmp = _service.GetBehaviors(_benali);
            Assert.That(forEmp.Count, Is.EqualTo(2));
            Assert.That(forEmp.Count(b => b.IsPositive), Is.EqualTo(1));
            Assert.That(_service.GetCompanyBehaviors(_companyId).Count, Is.EqualTo(2));
        }

        // ---- reports & smart ------------------------------------------------

        [Test]
        public void GetGeneralReport_AggregatesLatestEvaluations()
        {
            StarsTemplate();
            long period = NewPeriod();
            DoEvaluation(period, _benali, 5m); // 100
            DoEvaluation(period, _touati, 3m); // 60
            DoEvaluation(period, _saadi, 4m);  // 80

            GeneralReport r = _service.GetGeneralReport(_companyId);
            Assert.That(r.HasData, Is.True);
            Assert.That(r.EvaluatedCount, Is.EqualTo(3));
            Assert.That(r.CompanyAverage, Is.EqualTo(80m)); // (100+60+80)/3
            Assert.That(r.TopPerformers.First().EmployeeName, Does.Contain("BENALI"));
            Assert.That(r.BestEmployee, Is.Not.Null);
            Assert.That(r.Departments.Any(d => d.Department == "Production"), Is.True);
        }

        [Test]
        public void GetDeptReport_RanksAndFlagsSupport()
        {
            StarsTemplate();
            long period = NewPeriod();
            DoEvaluation(period, _benali, 5m); // Production 100
            DoEvaluation(period, _saadi, 2m);  // Production 40 → needs support (< 60)

            DeptReport r = _service.GetDeptReport(_companyId, "Production");
            Assert.That(r.Ranking.Count, Is.EqualTo(2));
            Assert.That(r.Ranking.First().Score, Is.EqualTo(100m), "best ranked first");
            Assert.That(r.NeedSupport.Any(x => x.Score < 60m), Is.True);
        }

        [Test]
        public void GetEmployeeReport_HasScoreStrengthsAndRecommendation()
        {
            StarsTemplate();
            long period = NewPeriod();
            DoEvaluation(period, _benali, 5m);

            EmployeeReport r = _service.GetEmployeeReport(_benali);
            Assert.That(r.HasData, Is.True);
            Assert.That(r.LatestScore, Is.EqualTo(100m));
            Assert.That(r.LatestBand, Is.EqualTo(ClassificationBand.Excellent));
            Assert.That(r.Strengths.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(r.RecommendationKey, Is.EqualTo("Perf_Reco_Promotion"));
        }

        [Test]
        public void GetReminders_ReturnsPendingEmployeesForAClosingPeriod()
        {
            StarsTemplate();
            var p = new EvalPeriod { CompanyId = _companyId, Name = "P", Cadence = PeriodCadence.Monthly, StartDate = DateTime.Today.AddDays(-20), EndDate = DateTime.Today.AddDays(2) };
            long period = _service.SavePeriod(p).Value;
            DoEvaluation(period, _benali, 4m); // benali done → not reminded

            var reminders = _service.GetReminders(_companyId, DateTime.Today);
            Assert.That(reminders.Count, Is.EqualTo(2), "the two employees still pending");
            Assert.That(reminders.All(x => x.EmployeeName != "BENALI Karim"), Is.True);
        }

        // ---- overview (control centre) --------------------------------------

        [Test]
        public void GetOverview_SummarisesMoversSupportBestAndActivity()
        {
            StarsTemplate();
            long p1 = NewPeriod();
            DoEvaluation(p1, _benali, 3m); // 60
            DoEvaluation(p1, _touati, 4m); // 80
            _service.ClosePeriod(p1);

            var p2p = new EvalPeriod { CompanyId = _companyId, Name = "Février", Cadence = PeriodCadence.Monthly, StartDate = new DateTime(2026, 2, 1), EndDate = new DateTime(2026, 2, 28) };
            long p2 = _service.SavePeriod(p2p).Value;
            DoEvaluation(p2, _benali, 5m); // 100 → improved (+40)
            DoEvaluation(p2, _touati, 2m); // 40 → declined + needs support
            _service.LogBehavior(_companyId, _benali, true, "bon travail", DateTime.Today);
            _service.ClosePeriod(p2);

            OverviewData d = _service.GetOverview(_companyId);
            Assert.That(d.HasData, Is.True);
            Assert.That(d.Improved.Any(m => m.EmployeeId == _benali && m.Delta > 0m), Is.True, "benali improved");
            Assert.That(d.Declined.Any(m => m.EmployeeId == _touati && m.Delta < 0m), Is.True, "touati declined");
            Assert.That(d.NeedSupport.Any(r => r.EmployeeId == _touati), Is.True, "touati needs support (< 60)");
            Assert.That(d.BestEmployee, Is.Not.Null);
            Assert.That(d.RecentActivity.Count, Is.GreaterThanOrEqualTo(1), "the behaviour appears in the activity feed");
        }
    }
}
