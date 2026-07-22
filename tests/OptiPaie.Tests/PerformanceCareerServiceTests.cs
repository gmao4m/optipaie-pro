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
    /// Performance &amp; Career expansion — integration tests against a real SQLite file:
    /// the built-in template library, template duplication/versioning, review cycles with
    /// live completion, goals, calibration, the company dashboard, comparison, the career
    /// timeline and cycle reminders. Proves migration 0022 applies and seeds the library.
    /// </summary>
    [TestFixture]
    public sealed class PerformanceCareerServiceTests
    {
        private static readonly int Year = DateTime.Today.Year - 1;

        private string _directory;
        private IUnitOfWorkFactory _uowFactory;
        private IPerformanceService _service;

        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-perfcareer-" + Guid.NewGuid().ToString("N"));
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

        private long AddEmployee(string last, string dept, bool active = true)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                uow.BeginTransaction();
                long id = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId,
                    LastNameFr = last,
                    FirstNameFr = "T",
                    Department = dept,
                    Gender = Gender.Male,
                    MaritalStatus = MaritalStatus.Single,
                    PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi,
                    HireDate = new DateTime(2020, 1, 1),
                    BaseSalary = 50000m,
                    Poste = "Agent",
                    IsActive = active
                });
                uow.Commit();
                return id;
            }
        }

        private List<PerformanceCriterion> CriteriaOf(long reviewId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                return uow.Performance.GetCriteria(reviewId).ToList();
            }
        }

        /// <summary>Scores every criterion of a review to the given value and finalises it.</summary>
        private void ScoreAndComplete(long reviewId, decimal score)
        {
            PerformanceReview review = _service.Get(reviewId);
            List<PerformanceCriterion> criteria = CriteriaOf(reviewId);
            foreach (PerformanceCriterion c in criteria) c.Score = score;

            review.Reviewer = review.Reviewer ?? "DRH";
            Result saved = _service.Save(review, criteria);
            Assert.That(saved.IsSuccess, Is.True, saved.Error);
            Result done = _service.Complete(reviewId);
            Assert.That(done.IsSuccess, Is.True, done.Error);
        }

        // ---------------------------------------------------------------- template library

        [Test]
        public void BuiltInLibrary_IsSeededWithSevenWeightedTemplates()
        {
            IReadOnlyList<TemplateSummary> templates = _service.GetTemplates(_companyId);
            List<TemplateSummary> builtIns = templates.Where(t => t.IsBuiltIn).ToList();

            Assert.That(builtIns.Count, Is.EqualTo(7), "seven built-in templates ship out of the box");
            Assert.That(builtIns.All(t => t.CriteriaCount > 0), Is.True);

            foreach (TemplateSummary t in builtIns)
            {
                TemplateDetail detail = _service.GetTemplateDetail(t.TemplateId);
                Assert.That(detail.WeightTotal, Is.EqualTo(100m), t.Name + " weights must sum to 100%");
            }
        }

        [Test]
        public void DuplicateTemplate_ProducesAnEditableCompanyCopy_LeavingTheBuiltInUntouched()
        {
            TemplateSummary sales = _service.GetTemplates(_companyId).First(t => t.Kind == TemplateKind.Sales);
            int builtInCriteria = _service.GetTemplateDetail(sales.TemplateId).Criteria.Count;

            Result<long> copy = _service.DuplicateTemplate(sales.TemplateId, _companyId, "Commercial DZ");
            Assert.That(copy.IsSuccess, Is.True, copy.Error);

            TemplateDetail copyDetail = _service.GetTemplateDetail(copy.Value);
            Assert.That(copyDetail.Template.IsBuiltIn, Is.False);
            Assert.That(copyDetail.Template.CompanyId, Is.EqualTo(_companyId));
            Assert.That(copyDetail.Criteria.Count, Is.EqualTo(builtInCriteria), "criteria copied across");

            // The built-in is still there and unmodified.
            Assert.That(_service.GetTemplateDetail(sales.TemplateId).Template.Name, Is.EqualTo(sales.Name));
        }

        [Test]
        public void SaveTemplate_RejectsWeightsNotSummingTo100()
        {
            var template = new PerformanceTemplate { CompanyId = _companyId, Name = "Bancal", Kind = TemplateKind.Custom, ScaleMax = 20m };
            var criteria = new List<PerformanceTemplateCriterion>
            {
                new PerformanceTemplateCriterion { Label = "A", WeightPercent = 40m },
                new PerformanceTemplateCriterion { Label = "B", WeightPercent = 40m }
            };

            Assert.That(_service.SaveTemplate(template, criteria).IsFailure, Is.True, "80% ≠ 100%");
        }

        [Test]
        public void SaveTemplate_OnceUsed_CreatesANewVersion_AndPreservesPastReviews()
        {
            long employee = AddEmployee("BENALI", "Commercial");

            // A company template.
            var template = new PerformanceTemplate { CompanyId = _companyId, Name = "Commercial v1", Kind = TemplateKind.Sales, ScaleMax = 20m };
            long templateId = _service.SaveTemplate(template, new List<PerformanceTemplateCriterion>
            {
                new PerformanceTemplateCriterion { Label = "Ventes", WeightPercent = 60m },
                new PerformanceTemplateCriterion { Label = "Clients", WeightPercent = 40m }
            }).Value;

            // Use it in a review (snapshots two criteria).
            long reviewId = _service.CreateFromTemplate(employee, templateId, Year, "S1", "DRH", null, null, null, false).Value;
            Assert.That(CriteriaOf(reviewId).Count, Is.EqualTo(2));

            // Edit the (now used) template -> a new version with three criteria.
            TemplateDetail current = _service.GetTemplateDetail(templateId);
            Result<long> edit = _service.SaveTemplate(current.Template, new List<PerformanceTemplateCriterion>
            {
                new PerformanceTemplateCriterion { Label = "Ventes", WeightPercent = 40m },
                new PerformanceTemplateCriterion { Label = "Clients", WeightPercent = 30m },
                new PerformanceTemplateCriterion { Label = "Upsell", WeightPercent = 30m }
            });
            Assert.That(edit.IsSuccess, Is.True, edit.Error);
            Assert.That(edit.Value, Is.Not.EqualTo(templateId), "a new version row was created");

            TemplateDetail v2 = _service.GetTemplateDetail(edit.Value);
            Assert.That(v2.Template.Version, Is.EqualTo(2));
            Assert.That(v2.Criteria.Count, Is.EqualTo(3));

            // The already-scored review kept its original two-criterion snapshot.
            Assert.That(CriteriaOf(reviewId).Count, Is.EqualTo(2), "past review untouched by the template edit");

            // The gallery shows only the current version for the group.
            List<TemplateSummary> visible = _service.GetTemplates(_companyId).Where(t => t.GroupKey == v2.Template.GroupKey).ToList();
            Assert.That(visible.Count, Is.EqualTo(1));
            Assert.That(visible[0].Version, Is.EqualTo(2));
        }

        [Test]
        public void BuiltInTemplate_CannotBeEditedInPlace()
        {
            TemplateSummary general = _service.GetTemplates(_companyId).First(t => t.Kind == TemplateKind.General);
            TemplateDetail detail = _service.GetTemplateDetail(general.TemplateId);

            Result<long> result = _service.SaveTemplate(detail.Template,
                detail.Criteria.Cast<PerformanceTemplateCriterion>().ToList());

            Assert.That(result.IsFailure, Is.True, "built-ins are read-only; duplicate first");
        }

        // ---------------------------------------------------------------- cycles

        [Test]
        public void LaunchCycle_ByDepartment_UsesTheDepartmentDefaultTemplateAndReviewer()
        {
            long manager = AddEmployee("CHEF", "Commercial");
            long seller1 = AddEmployee("VENDEUR1", "Commercial");
            long seller2 = AddEmployee("VENDEUR2", "Commercial");
            AddEmployee("USINE", "Production"); // excluded — different department

            _service.SaveDeptSetting(_companyId, "Commercial", "builtin-sales", manager);

            var request = new CycleLaunchRequest
            {
                CompanyId = _companyId,
                Name = "T1 " + Year,
                CycleType = PerformanceCycleType.Quarterly,
                StartDate = new DateTime(Year, 1, 1),
                EndDate = new DateTime(Year, 3, 31),
                Deadline = new DateTime(Year, 4, 15),
                PeriodYear = Year,
                PeriodLabel = "T1",
                Departments = new List<string> { "Commercial" }
            };

            Result<long> launched = _service.LaunchCycle(request);
            Assert.That(launched.IsSuccess, Is.True, launched.Error);

            CycleDetail detail = _service.GetCycleDetail(launched.Value);
            Assert.That(detail.Total, Is.EqualTo(3), "one review per Commercial employee, Production excluded");
            Assert.That(detail.CompletionPercent, Is.EqualTo(0m));

            CycleReviewRow row = detail.Reviews.First(r => r.EmployeeId == seller1);
            Assert.That(row.Reviewer, Is.EqualTo("CHEF T"), "reviewer resolved from the department default");
            Assert.That(row.DueDate.Value.Date, Is.EqualTo(new DateTime(Year, 4, 15)));

            // Seeded from the sales template (4 criteria).
            Assert.That(CriteriaOf(row.ReviewId).Count, Is.EqualTo(4));
            Assert.That(_service.Get(seller2 == 0 ? row.ReviewId : row.ReviewId).Kind, Is.EqualTo(TemplateKind.Sales));
        }

        [Test]
        public void Cycle_CompletionTracksToOneHundredPercent_AndFlipsStatus()
        {
            AddEmployee("A", "Admin");
            AddEmployee("B", "Admin");

            long cycleId = _service.LaunchCycle(new CycleLaunchRequest
            {
                CompanyId = _companyId,
                Name = "Annuel",
                CycleType = PerformanceCycleType.Annual,
                StartDate = new DateTime(Year, 1, 1),
                EndDate = new DateTime(Year, 12, 31),
                PeriodYear = Year,
                PeriodLabel = Year.ToString(),
                DefaultTemplateGroupKey = "builtin-general"
            }).Value;

            CycleDetail before = _service.GetCycleDetail(cycleId);
            Assert.That(before.Total, Is.EqualTo(2));

            foreach (CycleReviewRow r in before.Reviews) ScoreAndComplete(r.ReviewId, 15m);

            CycleSummary summary = _service.GetCycles(_companyId).First(c => c.CycleId == cycleId);
            Assert.That(summary.CompletionPercent, Is.EqualTo(100m));
            Assert.That(summary.Status, Is.EqualTo(PerformanceCycleStatus.Completed), "the cycle auto-completes when all reviews are in");
        }

        [Test]
        public void LaunchCycle_BulkAssignsAcrossHundredsOfEmployees()
        {
            const int count = 210;
            for (int i = 0; i < count; i++)
            {
                AddEmployee("EMP" + i, (i % 3 == 0) ? "Commercial" : (i % 3 == 1) ? "Production" : "Admin");
            }

            long cycleId = _service.LaunchCycle(new CycleLaunchRequest
            {
                CompanyId = _companyId,
                Name = "Campagne massive",
                CycleType = PerformanceCycleType.Quarterly,
                StartDate = new DateTime(Year, 1, 1),
                EndDate = new DateTime(Year, 3, 31),
                PeriodYear = Year,
                PeriodLabel = "T1",
                DefaultTemplateGroupKey = "builtin-general"
            }).Value;

            CycleDetail detail = _service.GetCycleDetail(cycleId);
            Assert.That(detail.Total, Is.EqualTo(count), "one review per employee at scale");
            Assert.That(detail.ByDepartment.Sum(d => d.Total), Is.EqualTo(count));
        }

        // ---------------------------------------------------------------- goals

        [Test]
        public void Goals_CapActiveGoalsAtFivePerEmployee()
        {
            long employee = AddEmployee("GOAL", "Admin");

            for (int i = 0; i < 5; i++)
            {
                Result<long> ok = _service.CreateGoal(new PerformanceGoal { EmployeeId = employee, Title = "Objectif " + i });
                Assert.That(ok.IsSuccess, Is.True, ok.Error);
            }

            Assert.That(_service.CreateGoal(new PerformanceGoal { EmployeeId = employee, Title = "Sixième" }).IsFailure, Is.True);
        }

        [Test]
        public void SetGoalProgress_AutoAchievesAtOneHundred()
        {
            long employee = AddEmployee("GOAL", "Admin");
            long goalId = _service.CreateGoal(new PerformanceGoal { EmployeeId = employee, Title = "Terminer" }).Value;

            _service.SetGoalProgress(goalId, 100m);

            GoalRow row = _service.GetGoals(employee).First(g => g.GoalId == goalId);
            Assert.That(row.Status, Is.EqualTo(PerformanceGoalStatus.Achieved));
            Assert.That(row.ProgressPercent, Is.EqualTo(100m));
        }

        // ---------------------------------------------------------------- calibration

        [Test]
        public void Calibration_FlagsLenientAndHarshDepartments()
        {
            long high = AddEmployee("HAUT", "Commercial");
            long low = AddEmployee("BAS", "Production");

            long r1 = _service.CreateFromTemplate(high, GeneralTemplateId(), Year, Year.ToString(), "DRH", null, null, null, false).Value;
            long r2 = _service.CreateFromTemplate(low, GeneralTemplateId(), Year, Year.ToString(), "DRH", null, null, null, false).Value;
            ScoreAndComplete(r1, 18m); // 90%
            ScoreAndComplete(r2, 8m);  // 40%

            CalibrationView view = _service.GetCalibration(_companyId, Year);
            Assert.That(view.ReviewCount, Is.EqualTo(2));
            Assert.That(view.CompanyAveragePercent, Is.EqualTo(65m));

            CalibrationDeptRow commercial = view.Departments.First(d => d.Department == "Commercial");
            CalibrationDeptRow production = view.Departments.First(d => d.Department == "Production");
            Assert.That(commercial.IsOutlierHigh, Is.True, "90% vs 65% company average is a lenient outlier");
            Assert.That(production.IsOutlierLow, Is.True, "40% vs 65% company average is a harsh outlier");
            Assert.That(commercial.Distribution[4], Is.EqualTo(1), "one Excellent in Commercial");
        }

        // ---------------------------------------------------------------- dashboard

        [Test]
        public void Dashboard_SurfacesTopAndBottomPerformersCompanyWide()
        {
            long best = AddEmployee("MEILLEUR", "Commercial");
            long worst = AddEmployee("PIRE", "Production");
            long mid = AddEmployee("MOYEN", "Admin");

            ScoreAndComplete(_service.CreateFromTemplate(best, GeneralTemplateId(), Year, Year.ToString(), "DRH", null, null, null, false).Value, 19m);
            ScoreAndComplete(_service.CreateFromTemplate(worst, GeneralTemplateId(), Year, Year.ToString(), "DRH", null, null, null, false).Value, 7m);
            ScoreAndComplete(_service.CreateFromTemplate(mid, GeneralTemplateId(), Year, Year.ToString(), "DRH", null, null, null, false).Value, 13m);

            PerformanceDashboard dash = _service.GetDashboard(_companyId);
            Assert.That(dash.ReviewCount, Is.EqualTo(3));
            Assert.That(dash.TopPerformers.First().EmployeeId, Is.EqualTo(best));
            Assert.That(dash.BottomPerformers.First().EmployeeId, Is.EqualTo(worst));
            Assert.That(dash.DepartmentAverages.Count, Is.EqualTo(3));
            Assert.That(dash.Trend.Any(t => t.Year == Year), Is.True);
        }

        // ---------------------------------------------------------------- comparison + career

        [Test]
        public void Compare_ShowsScoresAndGoalsSideBySide()
        {
            long a = AddEmployee("ALPHA", "Commercial");
            long b = AddEmployee("BETA", "Commercial");
            ScoreAndComplete(_service.CreateFromTemplate(a, GeneralTemplateId(), Year, Year.ToString(), "DRH", null, null, null, false).Value, 16m);
            _service.CreateGoal(new PerformanceGoal { EmployeeId = a, Title = "But", ProgressPercent = 50m });

            EmployeeComparison cmp = _service.Compare(new List<long> { a, b });
            Assert.That(cmp.Employees.Count, Is.EqualTo(2));

            ComparisonColumn colA = cmp.Employees.First(e => e.EmployeeId == a);
            ComparisonColumn colB = cmp.Employees.First(e => e.EmployeeId == b);
            Assert.That(colA.HasReviews, Is.True);
            Assert.That(colA.ActiveGoals, Is.EqualTo(1));
            Assert.That(colA.GoalCompletionPercent, Is.EqualTo(50m));
            Assert.That(colB.HasReviews, Is.False, "no reviews yet for BETA");
        }

        [Test]
        public void CareerTimeline_MergesReviewsGoalsPromotionsAndRewards()
        {
            long employee = AddEmployee("PARCOURS", "Commercial");
            ScoreAndComplete(_service.CreateFromTemplate(employee, GeneralTemplateId(), Year, Year.ToString(), "DRH", null, null, null, false).Value, 15m);
            _service.CreateGoal(new PerformanceGoal { EmployeeId = employee, Title = "Objectif" });
            _service.LogPromotion(employee, "Agent", "Chef d'équipe", DateTime.Today, "Excellente évaluation", null);
            _service.LogReward(employee, 25000m, "Prime de rendement", DateTime.Today, "Dépassement d'objectif");

            CareerTimeline timeline = _service.GetCareerTimeline(employee);
            Assert.That(timeline.Items.Count, Is.EqualTo(4));
            Assert.That(timeline.Items.Any(i => i.Kind == "review"), Is.True);
            Assert.That(timeline.Items.Any(i => i.Kind == "goal"), Is.True);
            Assert.That(timeline.Items.Any(i => i.Kind == "promotion"), Is.True);
            Assert.That(timeline.Items.Any(i => i.Kind == "reward"), Is.True);
        }

        // ---------------------------------------------------------------- reminders

        [Test]
        public void Reminders_SurfaceOverdueAndUpcomingReviews()
        {
            AddEmployee("RETARD", "Admin");
            AddEmployee("BIENTOT", "Admin");

            DateTime asOf = new DateTime(Year, 6, 10);
            long cycleId = _service.LaunchCycle(new CycleLaunchRequest
            {
                CompanyId = _companyId,
                Name = "Avec échéance",
                CycleType = PerformanceCycleType.Quarterly,
                StartDate = new DateTime(Year, 5, 1),
                EndDate = new DateTime(Year, 6, 30),
                Deadline = new DateTime(Year, 6, 13),
                PeriodYear = Year,
                PeriodLabel = "T2",
                DefaultTemplateGroupKey = "builtin-general"
            }).Value;

            IReadOnlyList<PerformanceReminder> reminders = _service.GetReminders(_companyId, asOf);
            Assert.That(reminders.Count, Is.EqualTo(2), "both pending reviews carry the cycle deadline");
            Assert.That(reminders.All(r => r.DaysLeft == 3 && !r.IsOverdue), Is.True, "3 days left on 10 June for a 13 June deadline");

            // After completing one, only the other remains pending.
            CycleDetail detail = _service.GetCycleDetail(cycleId);
            ScoreAndComplete(detail.Reviews.First().ReviewId, 14m);
            Assert.That(_service.GetReminders(_companyId, asOf).Count, Is.EqualTo(1));
        }

        [Test]
        public void CustomScaleTemplate_ScoresOnItsOwnScale_AndRatesByPercent()
        {
            long employee = AddEmployee("ECHELLE", "Technique");

            var template = new PerformanceTemplate { CompanyId = _companyId, Name = "Échelle /5", Kind = TemplateKind.Technical, ScaleMax = 5m };
            long templateId = _service.SaveTemplate(template, new List<PerformanceTemplateCriterion>
            {
                new PerformanceTemplateCriterion { Label = "Qualité", WeightPercent = 50m },
                new PerformanceTemplateCriterion { Label = "Délais", WeightPercent = 50m }
            }).Value;

            long reviewId = _service.CreateFromTemplate(employee, templateId, Year, Year.ToString(), "DRH", null, null, null, false).Value;
            Assert.That(_service.Get(reviewId).ScaleMax, Is.EqualTo(5m));

            List<PerformanceCriterion> criteria = CriteriaOf(reviewId);
            foreach (PerformanceCriterion c in criteria) c.Score = 4m; // 4/5 = 80% -> Excellent
            PerformanceReview header = _service.Get(reviewId);
            Assert.That(_service.Save(header, criteria).IsSuccess, Is.True);
            Assert.That(_service.Complete(reviewId).IsSuccess, Is.True);

            PerformanceDetail detail = _service.GetDetail(reviewId);
            Assert.That(detail.Review.OverallScore, Is.EqualTo(4m));
            Assert.That(detail.Rating, Is.EqualTo("Excellent"), "80% on the /5 scale is Excellent");

            // A score above the scale is rejected.
            criteria[0].Score = 6m;
            Assert.That(_service.Save(_service.Get(reviewId), criteria).IsFailure, Is.True, "6 > scale max of 5");
        }

        private long GeneralTemplateId()
        {
            return _service.GetTemplates(_companyId).First(t => t.Kind == TemplateKind.General).TemplateId;
        }
    }
}
