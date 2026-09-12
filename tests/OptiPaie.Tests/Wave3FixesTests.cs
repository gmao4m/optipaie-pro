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
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Wave 3 (half-done features) — non-regression tests against a real SQLite database.
    /// Each test fails if the corresponding defect returns.
    ///
    /// Honest scope note: some Wave 3 fixes live purely in the WPF Desktop view models — the
    /// recruitment menu CanExecute predicate, the evaluation-export shaping, and the includeInactive
    /// flags passed by the certificate/leave/report view models. The test project does not reference
    /// the Desktop assembly, so those are covered here only at the service/DTO layer they depend on
    /// (GetTypes, GetByCompany(includeInactive), GetEmployeeReport, AssetSummary.IsShared); the
    /// view-model wiring itself is verified by code review, not by these unit tests.
    /// </summary>
    [TestFixture]
    public sealed class Wave3FixesTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uowf;
        private LeaveService _leave;
        private AttendanceService _attendance;
        private AssetService _assets;
        private EmployeeService _employees;
        private PerformanceService _perf;

        private long _companyId, _benali, _touati, _saadi;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-wave3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uowf = new UnitOfWorkFactory(factory);
            _leave = new LeaveService(_uowf);
            _attendance = new AttendanceService(_uowf);
            _assets = new AssetService(_uowf);
            _employees = new EmployeeService(_uowf, new EmployeeValidator());
            _perf = new PerformanceService(_uowf);

            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _benali = Emp(uow, "BENALI", "Karim", "Production", active: true);
                _touati = Emp(uow, "TOUATI", "Lila", "Commercial", active: true);
                _saadi = Emp(uow, "SAADI", "Farid", "Production", active: true);
                uow.Commit();
            }
        }

        private long Emp(IUnitOfWork uow, string last, string first, string dept, bool active, DateTime? exit = null) =>
            uow.Employees.Insert(new Employee
            {
                CompanyId = _companyId, LastNameFr = last, FirstNameFr = first, Department = dept, Poste = "Poste",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2020, 1, 1), BaseSalary = 60000m,
                IsActive = active, ExitDate = exit
            });

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ── #1 (IDX 32) : the configurable leave-type catalogue is offered AND recorded on a request ──

        [Test]
        public void LeaveTypes_TheCatalogueIsAvailable_ToBeOffered()
        {
            IReadOnlyList<LeaveTypeDefinition> types = _leave.GetTypes(_companyId);
            Assert.That(types.Count, Is.GreaterThanOrEqualTo(5), "the configurable catalogue must be seeded and returned");
            Assert.That(types.Any(t => t.PaymentCategory == PaymentCategory.SocialSecurity), Is.True,
                "a CNAS-paid type (e.g. maladie/maternité) must exist so it can be chosen — not only the 5 enum values");
        }

        [Test]
        public void LeaveRequest_RecordsTheChosenConfigurableType_NotJustTheEnum()
        {
            LeaveTypeDefinition sick = _leave.GetTypes(_companyId).First(t => t.PaymentCategory == PaymentCategory.SocialSecurity);

            var request = new LeaveRequest
            {
                EmployeeId = _benali,
                Type = sick.BaseType,        // legacy 1..5 column stays satisfied
                LeaveTypeId = sick.Id,       // the configurable type the form now records
                StartDate = new DateTime(2026, 3, 2),
                EndDate = new DateTime(2026, 3, 6),
                IsDraft = true
            };

            Result<long> saved = _leave.Save(request);
            Assert.That(saved.IsSuccess, Is.True, saved.Error);

            LeaveRequest back = _leave.Get(saved.Value);
            Assert.That(back.LeaveTypeId, Is.EqualTo(sick.Id),
                "the request must persist the configurable LeaveTypeId (the old form left it null)");
        }

        // ── #2 (IDX 30) : the matrix "gomme" erases a day; is idempotent; protects a synced leave ──

        [Test]
        public void ClearDay_ErasesAPaintedDay_AndIsIdempotent()
        {
            DateTime day = new DateTime(2026, 3, 3);
            Assert.That(_attendance.SetDayStatus(_benali, day, AttendanceStatus.Present).IsSuccess, Is.True);
            Assert.That(_attendance.Get(_benali, day), Is.Not.Null);

            Assert.That(_attendance.ClearDay(_benali, day).IsSuccess, Is.True, "a mispainted cell can be erased");
            Assert.That(_attendance.Get(_benali, day), Is.Null, "the record is gone");

            Assert.That(_attendance.ClearDay(_benali, day).IsSuccess, Is.True, "erasing an empty day is a harmless no-op");
        }

        [Test]
        public void ClearDay_RefusesToEraseADaySyncedFromALeave()
        {
            DateTime day = new DateTime(2026, 3, 4);
            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                uow.Attendance.Insert(new AttendanceRecord
                {
                    EmployeeId = _benali, WorkDate = day, Status = AttendanceStatus.Leave, Notes = "[Congé] Congé annuel"
                });
                uow.Commit();
            }

            Result result = _attendance.ClearDay(_benali, day);
            Assert.That(result.IsFailure, Is.True, "a synced leave must be undone from the Leave module, never erased here");
            Assert.That(result.ErrorCode, Is.EqualTo("Attendance_LeaveLinkedClear"));
            Assert.That(_attendance.Get(_benali, day), Is.Not.Null, "the leave day is still present");
        }

        // ── #3 (IDX 7) : a shared asset stays assignable — its summary carries IsShared for the UI gate ──

        [Test]
        public void SharedAsset_SummaryExposesIsShared_AndStaysAssignedWithAHolder()
        {
            long shared = _assets.Save(SharedAsset("Véhicule de pool")).Value;
            Assert.That(_assets.Assign(shared, _benali, new DateTime(2026, 1, 10), "RAS", null).IsSuccess, Is.True);

            AssetSummary s = _assets.GetSummary(shared);
            Assert.That(s.IsShared, Is.True, "the summary must expose IsShared so « Attribuer » stays enabled for a pool asset");
            Assert.That(s.Status, Is.EqualTo(AssetStatus.Assigned), "shared asset is Assigned while a holder remains");

            long exclusive = _assets.Save(NewAsset("Laptop", AssetCategory.Laptop, 100000m)).Value;
            Assert.That(_assets.GetSummary(exclusive).IsShared, Is.False);
        }

        // ── #4 (IDX 44) : the employee report carries the criterion detail the export now renders ──

        [Test]
        public void EmployeeReport_YieldsCriterionScores_SoTheEmployeeExportIsNotEmpty()
        {
            long period = NewPeriod(PeriodStatus.Open);
            StarsTemplate();
            DoEvaluation(period, _saadi);

            EmployeeReport report = _perf.GetEmployeeReport(_saadi);
            Assert.That(report.HasData, Is.True);
            Assert.That(report.Strengths.Count + report.Weaknesses.Count, Is.GreaterThan(0),
                "the per-employee export builds its table from these criterion scores");
        }

        // ── #6 (IDX evaluation lock) : a CLOSED period freezes its evaluations; an empty eval cannot complete ──

        [Test]
        public void ClosedPeriod_FreezesEvaluationMutations()
        {
            StarsTemplate();
            long period = NewPeriod(PeriodStatus.Open);
            long evalId = DoEvaluation(period, _benali);

            _perf.ClosePeriod(period);

            EvaluationDetail d = _perf.GetEvaluationDetail(evalId);
            Assert.That(_perf.SaveEvaluation(d.Evaluation, d.Scores).ErrorCode, Is.EqualTo("Performance_PeriodClosed"));
            Assert.That(_perf.ReopenEvaluation(evalId).ErrorCode, Is.EqualTo("Performance_PeriodClosed"));
            Assert.That(_perf.DeleteEvaluation(evalId).ErrorCode, Is.EqualTo("Performance_PeriodClosed"));
        }

        [Test]
        public void CompletingAnUnscoredEvaluation_IsRefused()
        {
            StarsTemplate();
            long period = NewPeriod(PeriodStatus.Open);
            long evalId = _perf.CreateEvaluation(period, _touati, null).Value;

            // No criterion scored at all.
            Assert.That(_perf.CompleteEvaluation(evalId).ErrorCode, Is.EqualTo("Performance_EvaluationEmpty"));
        }

        [Test]
        public void CompletingAKpiScoredEvaluation_Succeeds_NotWronglyRejectedAsEmpty()
        {
            KpiTemplate();
            long period = NewPeriod(PeriodStatus.Open);
            long evalId = _perf.CreateEvaluation(period, _benali, null).Value;

            EvaluationDetail d = _perf.GetEvaluationDetail(evalId);
            var scores = d.Scores.ToList();
            Assert.That(scores.Any(s => s.Category == CriterionCategory.Kpi), Is.True, "the template must yield a KPI line");
            foreach (EvaluationScore s in scores)
            {
                s.KpiActual = 90m;                       // KPI criteria are scored via KpiActual, never RawValue
                s.NormalizedScore = _perf.ComputeLineScore(s);
            }
            Assert.That(_perf.SaveEvaluation(d.Evaluation, scores).IsSuccess, Is.True);

            // Regression guard: the empty-guard must accept KPI scoring. With the old RawValue-only
            // check this returned Performance_EvaluationEmpty and the KPI evaluation could never finish.
            Assert.That(_perf.CompleteEvaluation(evalId).IsSuccess, Is.True,
                "a fully-scored KPI evaluation must be completable");
        }

        // ── #7 (IDX 7 / certificat) : a departed employee stays reachable when inactive is included ──

        [Test]
        public void DepartedEmployee_IsListed_WhenInactiveAreIncluded()
        {
            long gone;
            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                gone = Emp(uow, "KADRI", "Nadia", "Production", active: false, exit: new DateTime(2026, 2, 28));
                uow.Commit();
            }

            Assert.That(_employees.GetByCompany(_companyId, true).Any(e => e.Id == gone), Is.True,
                "a certificat de travail must be issuable for someone who has left → they stay selectable");
            Assert.That(_employees.GetByCompany(_companyId, false).Any(e => e.Id == gone), Is.False,
                "but the active-only list still excludes them");
        }

        // ---- helpers --------------------------------------------------------

        private long StarsTemplate()
        {
            var crits = new List<EvalCriterion>
            {
                new EvalCriterion { Name = "Qualité", Category = CriterionCategory.Behavioral, ScoreType = ScoreType.Stars5, WeightPercent = 50m },
                new EvalCriterion { Name = "Assiduité", Category = CriterionCategory.Behavioral, ScoreType = ScoreType.Stars5, WeightPercent = 50m }
            };
            var t = new EvalTemplate { CompanyId = _companyId, Name = "Grille", Department = "Production", WeightingMode = WeightingMode.Simple, IsDefault = true };
            Result<long> r = _perf.SaveTemplate(t, crits);
            Assert.That(r.IsSuccess, Is.True, r.Error);
            return r.Value;
        }

        private long KpiTemplate()
        {
            var crits = new List<EvalCriterion>
            {
                new EvalCriterion { Name = "Ventes", Category = CriterionCategory.Kpi, ScoreType = ScoreType.Percent, WeightPercent = 100m, KpiTarget = 100m, HigherIsBetter = true }
            };
            var t = new EvalTemplate { CompanyId = _companyId, Name = "Grille KPI", Department = "Production", WeightingMode = WeightingMode.Simple, IsDefault = true };
            Result<long> r = _perf.SaveTemplate(t, crits);
            Assert.That(r.IsSuccess, Is.True, r.Error);
            return r.Value;
        }

        private long NewPeriod(PeriodStatus status)
        {
            var p = new EvalPeriod { CompanyId = _companyId, Name = "Janvier", Cadence = PeriodCadence.Monthly, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 1, 31) };
            long id = _perf.SavePeriod(p).Value;
            if (status == PeriodStatus.Closed) _perf.ClosePeriod(id);
            return id;
        }

        /// <summary>Scores the two criteria differently (so strengths ≠ weaknesses) and completes.</summary>
        private long DoEvaluation(long periodId, long employeeId)
        {
            long evalId = _perf.CreateEvaluation(periodId, employeeId, null).Value;
            EvaluationDetail d = _perf.GetEvaluationDetail(evalId);
            var scores = d.Scores.ToList();
            decimal stars = 5m;
            foreach (EvaluationScore s in scores)
            {
                s.RawValue = stars;
                s.NormalizedScore = _perf.ComputeLineScore(s);
                stars = 2m; // second criterion lower → a distinct strength and weakness
            }
            Assert.That(_perf.SaveEvaluation(d.Evaluation, scores).IsSuccess, Is.True);
            Assert.That(_perf.CompleteEvaluation(evalId).IsSuccess, Is.True);
            return evalId;
        }

        private Asset SharedAsset(string name)
        {
            Asset a = NewAsset(name, AssetCategory.Vehicle, 2000000m);
            a.IsShared = true;
            return a;
        }

        private Asset NewAsset(string name, AssetCategory category, decimal value) =>
            new Asset
            {
                CompanyId = _companyId,
                Name = name,
                Category = category,
                PurchaseValue = value,
                PurchaseDate = new DateTime(2025, 1, 1),
                SerialNumber = "SN-" + Guid.NewGuid().ToString("N").Substring(0, 8)
            };
    }
}
