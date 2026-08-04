using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// The Performance (Évaluation) module service. All the fairness rules live here in ONE
    /// place: every criterion is normalised to 0-100 whatever its type (stars / 20 / percent /
    /// KPI achievement), then combined equally (simple mode) or by weight (weighted mode) into a
    /// single 0-100 total mapped to a five-band classification. Also owns the continuous
    /// behaviour log, evaluation periods, report aggregation and the "smart" hints. Reads
    /// employee/department data from the shared tables; never writes payroll.
    /// </summary>
    public sealed class PerformanceService : IPerformanceService
    {
        private readonly IUnitOfWorkFactory _uowFactory;

        // Fairness band thresholds on the 0-100 scale (inclusive lower bounds).
        private const decimal BandExcellent = 90m;
        private const decimal BandVeryGood = 75m;
        private const decimal BandGood = 60m;
        private const decimal BandAverage = 45m;

        // "Needs support" cutoff.
        private const decimal SupportBelow = 60m;

        public PerformanceService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _uowFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        }

        // ===================== TEMPLATES =====================================

        public IReadOnlyList<TemplateSummary> GetTemplates(long companyId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                var templates = uow.Performance.GetTemplatesForCompany(companyId).ToList();
                var list = new List<TemplateSummary>();
                foreach (EvalTemplate t in templates)
                {
                    int count = uow.Performance.GetCriteria(t.Id).Count();
                    list.Add(new TemplateSummary
                    {
                        TemplateId = t.Id,
                        Name = t.Name,
                        Department = t.Department,
                        WeightingMode = t.WeightingMode,
                        CriteriaCount = count,
                        IsBuiltIn = t.IsBuiltIn,
                        IsDefault = t.IsDefault
                    });
                }
                return list;
            }
        }

        public TemplateDetail GetTemplateDetail(long templateId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                EvalTemplate t = uow.Performance.GetTemplate(templateId);
                if (t == null) return null;
                return new TemplateDetail
                {
                    Template = t,
                    Criteria = uow.Performance.GetCriteria(templateId).ToList()
                };
            }
        }

        public EvalTemplate ResolveTemplate(long companyId, string department)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
                return ResolveTemplate(uow, companyId, department);
        }

        private static EvalTemplate ResolveTemplate(IUnitOfWork uow, long companyId, string department)
        {
            var all = uow.Performance.GetTemplatesForCompany(companyId).ToList();
            var company = all.Where(t => t.CompanyId == companyId).ToList();

            // 1. a company template whose department matches the employee's
            EvalTemplate match = company.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.Department) && !string.IsNullOrEmpty(department) &&
                string.Equals(t.Department.Trim(), department.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // 2. the company default
            match = company.FirstOrDefault(t => t.IsDefault);
            if (match != null) return match;

            // 3. any company template
            if (company.Count > 0) return company[0];

            // 4. the built-in default, then any built-in
            var builtIns = all.Where(t => t.CompanyId == null).ToList();
            return builtIns.FirstOrDefault(t => t.IsDefault) ?? builtIns.FirstOrDefault();
        }

        public Result<long> SaveTemplate(EvalTemplate template, IEnumerable<EvalCriterion> criteria)
        {
            if (template == null) return Result.Fail<long>("Template requis.", "Performance_TemplateRequired");
            if (string.IsNullOrWhiteSpace(template.Name))
                return Result.Fail<long>("Le nom du modèle est requis.", "Performance_NameRequired");
            if (template.IsBuiltIn)
                return Result.Fail<long>("Un modèle intégré est en lecture seule.", "Performance_TemplateBuiltInReadOnly");

            var lines = (criteria ?? Enumerable.Empty<EvalCriterion>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
            if (lines.Count == 0)
                return Result.Fail<long>("Ajoutez au moins un critère.", "Performance_NoCriteria");

            if (template.WeightingMode == WeightingMode.Weighted)
            {
                decimal sum = lines.Sum(c => c.WeightPercent);
                if (Math.Abs(sum - 100m) > 0.5m)
                    return Result.Fail<long>("La somme des pondérations doit faire 100 %.", "Performance_TemplateWeightSum");
            }

            using (IUnitOfWork uow = _uowFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    template.IsBuiltIn = false;
                    long id;
                    if (template.Id > 0)
                    {
                        uow.Performance.UpdateTemplate(template);
                        uow.Performance.DeleteCriteria(template.Id);
                        id = template.Id;
                    }
                    else
                    {
                        id = uow.Performance.InsertTemplate(template);
                    }

                    if (template.IsDefault)
                    {
                        uow.Performance.ClearDefaultTemplate(template.CompanyId ?? 0);
                        template.Id = id;
                        template.IsDefault = true;
                        uow.Performance.UpdateTemplate(template);
                    }

                    int order = 0;
                    foreach (EvalCriterion c in lines)
                    {
                        c.TemplateId = id;
                        c.SortOrder = order++;
                        if (c.WeightPercent < 0m) c.WeightPercent = 0m;
                        uow.Performance.InsertCriterion(c);
                    }

                    uow.Commit();
                    return Result.Ok(id);
                }
                catch { uow.Rollback(); throw; }
            }
        }

        public Result<long> DuplicateTemplate(long sourceTemplateId, long companyId, string newName, string department)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                EvalTemplate src = uow.Performance.GetTemplate(sourceTemplateId);
                if (src == null) return Result.Fail<long>("Modèle introuvable.", "Performance_TemplateNotFound");

                var srcCriteria = uow.Performance.GetCriteria(sourceTemplateId).ToList();
                uow.BeginTransaction();
                try
                {
                    var copy = new EvalTemplate
                    {
                        CompanyId = companyId,
                        Department = department,
                        Name = string.IsNullOrWhiteSpace(newName) ? (src.Name + " (copie)") : newName,
                        Description = src.Description,
                        WeightingMode = src.WeightingMode,
                        IsBuiltIn = false,
                        IsDefault = false
                    };
                    long id = uow.Performance.InsertTemplate(copy);
                    int order = 0;
                    foreach (EvalCriterion c in srcCriteria)
                    {
                        uow.Performance.InsertCriterion(new EvalCriterion
                        {
                            TemplateId = id,
                            Name = c.Name,
                            Category = c.Category,
                            ScoreType = c.ScoreType,
                            WeightPercent = c.WeightPercent,
                            KpiTarget = c.KpiTarget,
                            HigherIsBetter = c.HigherIsBetter,
                            SortOrder = order++
                        });
                    }
                    uow.Commit();
                    return Result.Ok(id);
                }
                catch { uow.Rollback(); throw; }
            }
        }

        public Result SetDefaultTemplate(long companyId, long templateId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                EvalTemplate t = uow.Performance.GetTemplate(templateId);
                if (t == null) return Result.Fail("Modèle introuvable.", "Performance_TemplateNotFound");
                if (t.CompanyId != companyId)
                    return Result.Fail("Ce modèle n'appartient pas à l'entreprise.", "Performance_TemplateNotOwned");

                uow.BeginTransaction();
                try
                {
                    uow.Performance.ClearDefaultTemplate(companyId);
                    t.IsDefault = true;
                    uow.Performance.UpdateTemplate(t);
                    uow.Commit();
                    return Result.Ok();
                }
                catch { uow.Rollback(); throw; }
            }
        }

        public Result DeleteTemplate(long templateId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                EvalTemplate t = uow.Performance.GetTemplate(templateId);
                if (t == null) return Result.Fail("Modèle introuvable.", "Performance_TemplateNotFound");
                if (t.IsBuiltIn)
                    return Result.Fail("Un modèle intégré est en lecture seule.", "Performance_TemplateBuiltInReadOnly");
                uow.Performance.SoftDeleteTemplate(templateId);
                return Result.Ok();
            }
        }

        // ===================== PERIODS =======================================

        public IReadOnlyList<PeriodSummary> GetPeriods(long companyId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                var periods = uow.Performance.GetPeriodsByCompany(companyId).ToList();
                var list = new List<PeriodSummary>();
                foreach (EvalPeriod p in periods)
                {
                    var evals = uow.Performance.GetEvaluationsByPeriod(p.Id).ToList();
                    list.Add(new PeriodSummary
                    {
                        PeriodId = p.Id,
                        Name = p.Name,
                        Cadence = p.Cadence,
                        StartDate = p.StartDate,
                        EndDate = p.EndDate,
                        Status = p.Status,
                        Total = evals.Count,
                        Done = evals.Count(e => e.Status == EvaluationStatus.Done)
                    });
                }
                return list;
            }
        }

        public EvalPeriod GetPeriod(long periodId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
                return uow.Performance.GetPeriod(periodId);
        }

        public Result<long> SavePeriod(EvalPeriod period)
        {
            if (period == null) return Result.Fail<long>("Période requise.", "Performance_PeriodRequired");
            if (string.IsNullOrWhiteSpace(period.Name))
                return Result.Fail<long>("Le nom de la période est requis.", "Performance_NameRequired");
            if (period.EndDate < period.StartDate)
                return Result.Fail<long>("La date de fin précède la date de début.", "Performance_PeriodDates");

            using (IUnitOfWork uow = _uowFactory.Create())
            {
                if (period.Id > 0)
                {
                    uow.Performance.UpdatePeriod(period);
                    return Result.Ok(period.Id);
                }
                long id = uow.Performance.InsertPeriod(period);
                return Result.Ok(id);
            }
        }

        public Result ClosePeriod(long periodId) => SetPeriodStatus(periodId, PeriodStatus.Closed);

        public Result ReopenPeriod(long periodId) => SetPeriodStatus(periodId, PeriodStatus.Open);

        private Result SetPeriodStatus(long periodId, PeriodStatus status)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                EvalPeriod p = uow.Performance.GetPeriod(periodId);
                if (p == null) return Result.Fail("Période introuvable.", "Performance_PeriodNotFound");
                p.Status = status;
                uow.Performance.UpdatePeriod(p);
                return Result.Ok();
            }
        }

        public Result DeletePeriod(long periodId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                EvalPeriod p = uow.Performance.GetPeriod(periodId);
                if (p == null) return Result.Fail("Période introuvable.", "Performance_PeriodNotFound");
                uow.Performance.SoftDeletePeriod(periodId);
                return Result.Ok();
            }
        }

        // ===================== EVALUATIONS ===================================

        public IReadOnlyList<EvaluationSummary> GetEvaluationBoard(long periodId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                EvalPeriod period = uow.Performance.GetPeriod(periodId);
                if (period == null) return new List<EvaluationSummary>();

                var employees = uow.Employees.GetByCompany(period.CompanyId, includeInactive: false).ToList();
                var byEmployee = uow.Performance.GetEvaluationsByPeriod(periodId)
                    .GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.First());

                var rows = new List<EvaluationSummary>();
                foreach (Employee emp in employees)
                {
                    byEmployee.TryGetValue(emp.Id, out Evaluation ev);
                    rows.Add(new EvaluationSummary
                    {
                        EvaluationId = ev?.Id ?? 0,
                        EmployeeId = emp.Id,
                        EmployeeName = FullName(emp),
                        Department = emp.Department,
                        PeriodId = period.Id,
                        PeriodName = period.Name,
                        PeriodEnd = period.EndDate,
                        TotalScore = ev?.TotalScore ?? 0m,
                        Band = Classify(ev?.TotalScore ?? 0m),
                        Status = ev?.Status ?? EvaluationStatus.Pending,
                        EvaluatedDate = ev?.EvaluatedDate
                    });
                }
                return rows.OrderBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        public EvaluationDetail GetEvaluationDetail(long evaluationId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Evaluation ev = uow.Performance.GetEvaluation(evaluationId);
                if (ev == null) return null;
                Employee emp = uow.Employees.GetById(ev.EmployeeId);
                EvalPeriod period = uow.Performance.GetPeriod(ev.PeriodId);
                var scores = uow.Performance.GetScores(evaluationId).ToList();

                var behaviors = period == null ? new List<BehaviorLog>()
                    : uow.Performance.GetBehaviorsInRange(ev.EmployeeId, period.StartDate, period.EndDate).ToList();

                return new EvaluationDetail
                {
                    Evaluation = ev,
                    Scores = scores,
                    EmployeeName = emp == null ? string.Empty : FullName(emp),
                    EmployeeMeta = emp == null ? string.Empty : Join(emp.Poste, emp.Department),
                    PeriodName = period?.Name,
                    Behaviors = behaviors.Select(b => ToEntry(b, emp == null ? string.Empty : FullName(emp))).ToList(),
                    PositiveCount = behaviors.Count(b => b.IsPositive),
                    NegativeCount = behaviors.Count(b => !b.IsPositive),
                    Band = Classify(ev.TotalScore)
                };
            }
        }

        public Result<long> CreateEvaluation(long periodId, long employeeId, long? templateId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                EvalPeriod period = uow.Performance.GetPeriod(periodId);
                if (period == null) return Result.Fail<long>("Période introuvable.", "Performance_PeriodNotFound");
                if (period.Status == PeriodStatus.Closed)
                    return Result.Fail<long>("La période est clôturée.", "Performance_PeriodClosed");

                Employee emp = uow.Employees.GetById(employeeId);
                if (emp == null) return Result.Fail<long>("Employé introuvable.", "Performance_EmployeeNotFound");

                Evaluation existing = uow.Performance.GetForEmployeeInPeriod(periodId, employeeId);
                if (existing != null) return Result.Ok(existing.Id);

                EvalTemplate template = templateId.HasValue
                    ? uow.Performance.GetTemplate(templateId.Value)
                    : ResolveTemplate(uow, period.CompanyId, emp.Department);
                var criteria = template != null
                    ? uow.Performance.GetCriteria(template.Id).ToList()
                    : new List<EvalCriterion>();

                uow.BeginTransaction();
                try
                {
                    var ev = new Evaluation
                    {
                        PeriodId = periodId,
                        EmployeeId = employeeId,
                        TemplateId = template?.Id,
                        Department = emp.Department,
                        WeightingMode = template?.WeightingMode ?? WeightingMode.Simple,
                        TotalScore = 0m,
                        Status = EvaluationStatus.Pending
                    };
                    long id = uow.Performance.InsertEvaluation(ev);

                    int order = 0;
                    foreach (EvalCriterion c in criteria)
                    {
                        uow.Performance.InsertScore(new EvaluationScore
                        {
                            EvaluationId = id,
                            CriterionName = c.Name,
                            Category = c.Category,
                            ScoreType = c.ScoreType,
                            WeightPercent = c.WeightPercent,
                            RawValue = null,
                            KpiTarget = c.KpiTarget,
                            KpiActual = null,
                            HigherIsBetter = c.HigherIsBetter,
                            NormalizedScore = 0m,
                            SortOrder = order++
                        });
                    }
                    uow.Commit();
                    return Result.Ok(id);
                }
                catch { uow.Rollback(); throw; }
            }
        }

        public Result SaveEvaluation(Evaluation evaluation, IEnumerable<EvaluationScore> scores)
        {
            if (evaluation == null) return Result.Fail("Évaluation requise.", "Performance_EvaluationRequired");
            var lines = (scores ?? Enumerable.Empty<EvaluationScore>()).ToList();

            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Evaluation existing = uow.Performance.GetEvaluation(evaluation.Id);
                if (existing == null) return Result.Fail("Évaluation introuvable.", "Performance_EvaluationNotFound");

                foreach (EvaluationScore line in lines)
                    line.NormalizedScore = ComputeLineScore(line);
                evaluation.TotalScore = ComputeTotal(lines, evaluation.WeightingMode);

                uow.BeginTransaction();
                try
                {
                    uow.Performance.UpdateEvaluation(evaluation);
                    uow.Performance.DeleteScores(evaluation.Id);
                    int order = 0;
                    foreach (EvaluationScore line in lines)
                    {
                        line.EvaluationId = evaluation.Id;
                        line.SortOrder = order++;
                        uow.Performance.InsertScore(line);
                    }
                    uow.Commit();
                    return Result.Ok();
                }
                catch { uow.Rollback(); throw; }
            }
        }

        public Result CompleteEvaluation(long evaluationId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Evaluation ev = uow.Performance.GetEvaluation(evaluationId);
                if (ev == null) return Result.Fail("Évaluation introuvable.", "Performance_EvaluationNotFound");
                var lines = uow.Performance.GetScores(evaluationId).ToList();
                ev.TotalScore = ComputeTotal(lines, ev.WeightingMode);
                ev.Status = EvaluationStatus.Done;
                ev.EvaluatedDate = DateTime.Today;
                uow.Performance.UpdateEvaluation(ev);
                return Result.Ok();
            }
        }

        public Result ReopenEvaluation(long evaluationId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Evaluation ev = uow.Performance.GetEvaluation(evaluationId);
                if (ev == null) return Result.Fail("Évaluation introuvable.", "Performance_EvaluationNotFound");
                ev.Status = EvaluationStatus.Pending;
                uow.Performance.UpdateEvaluation(ev);
                return Result.Ok();
            }
        }

        public Result DeleteEvaluation(long evaluationId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Evaluation ev = uow.Performance.GetEvaluation(evaluationId);
                if (ev == null) return Result.Fail("Évaluation introuvable.", "Performance_EvaluationNotFound");
                uow.Performance.SoftDeleteEvaluation(evaluationId);
                return Result.Ok();
            }
        }

        // ----- scoring helpers (pure) ----------------------------------------

        public decimal ComputeLineScore(EvaluationScore line)
        {
            if (line == null) return 0m;

            if (line.Category == CriterionCategory.Kpi)
                return ScoreKpi(line.KpiTarget, line.KpiActual, line.HigherIsBetter);

            if (!line.RawValue.HasValue) return 0m;
            decimal raw = line.RawValue.Value;
            decimal pct;
            switch (line.ScoreType)
            {
                case ScoreType.Stars5: pct = raw / 5m * 100m; break;
                case ScoreType.Score20: pct = raw / 20m * 100m; break;
                default: pct = raw; break; // Percent
            }
            return Clamp(Math.Round(pct, 2, MidpointRounding.AwayFromZero));
        }

        private static decimal ScoreKpi(decimal? target, decimal? achieved, bool higherIsBetter)
        {
            if (!target.HasValue || !achieved.HasValue) return 0m;
            decimal t = target.Value, a = achieved.Value;
            decimal pct;
            if (higherIsBetter)
                pct = t <= 0m ? (a > 0m ? 100m : 0m) : a / t * 100m;
            else
                pct = a <= 0m ? 100m : (t <= 0m ? 0m : t / a * 100m);
            return Clamp(Math.Round(pct, 2, MidpointRounding.AwayFromZero));
        }

        public decimal ComputeTotal(IReadOnlyList<EvaluationScore> scores, WeightingMode mode)
        {
            if (scores == null || scores.Count == 0) return 0m;
            decimal total;
            if (mode == WeightingMode.Weighted)
            {
                decimal weight = scores.Sum(s => s.WeightPercent);
                total = weight > 0m
                    ? scores.Sum(s => s.NormalizedScore * s.WeightPercent) / weight
                    : scores.Average(s => s.NormalizedScore);
            }
            else
            {
                total = scores.Average(s => s.NormalizedScore);
            }
            return Clamp(Math.Round(total, 1, MidpointRounding.AwayFromZero));
        }

        public ClassificationBand Classify(decimal totalScore)
        {
            if (totalScore >= BandExcellent) return ClassificationBand.Excellent;
            if (totalScore >= BandVeryGood) return ClassificationBand.VeryGood;
            if (totalScore >= BandGood) return ClassificationBand.Good;
            if (totalScore >= BandAverage) return ClassificationBand.Average;
            return ClassificationBand.Weak;
        }

        // ===================== BEHAVIOUR LOG =================================

        public Result<long> LogBehavior(long companyId, long employeeId, bool isPositive, string note, DateTime occurredAt)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                if (!uow.Employees.ExistsById(employeeId))
                    return Result.Fail<long>("Employé introuvable.", "Performance_EmployeeNotFound");
                long id = uow.Performance.InsertBehavior(new BehaviorLog
                {
                    CompanyId = companyId,
                    EmployeeId = employeeId,
                    IsPositive = isPositive,
                    Note = note,
                    OccurredAt = occurredAt == default(DateTime) ? DateTime.Today : occurredAt
                });
                return Result.Ok(id);
            }
        }

        public IReadOnlyList<BehaviorEntry> GetBehaviors(long employeeId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Employee emp = uow.Employees.GetById(employeeId);
                string name = emp == null ? string.Empty : FullName(emp);
                return uow.Performance.GetBehaviorsByEmployee(employeeId)
                    .Select(b => ToEntry(b, name)).ToList();
            }
        }

        public IReadOnlyList<BehaviorEntry> GetCompanyBehaviors(long companyId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                var names = uow.Employees.GetByCompany(companyId).ToDictionary(e => e.Id, FullName);
                return uow.Performance.GetBehaviorsByCompany(companyId)
                    .Select(b => ToEntry(b, names.TryGetValue(b.EmployeeId, out string n) ? n : string.Empty))
                    .ToList();
            }
        }

        public Result DeleteBehavior(long behaviorId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                if (uow.Performance.GetBehavior(behaviorId) == null)
                    return Result.Fail("Élément introuvable.", "Performance_BehaviorNotFound");
                uow.Performance.SoftDeleteBehavior(behaviorId);
                return Result.Ok();
            }
        }

        // ===================== REPORTS & SMART ===============================

        public EmployeeReport GetEmployeeReport(long employeeId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Employee emp = uow.Employees.GetById(employeeId);
                var report = new EmployeeReport
                {
                    EmployeeId = employeeId,
                    EmployeeName = emp == null ? string.Empty : FullName(emp),
                    Department = emp?.Department,
                    Poste = emp?.Poste
                };

                var done = uow.Performance.GetEvaluationsByEmployee(employeeId)
                    .Where(e => e.Status == EvaluationStatus.Done).ToList();
                var periods = uow.Performance.GetPeriodsByCompany(emp?.CompanyId ?? 0)
                    .ToDictionary(p => p.Id, p => p);

                var behaviors = uow.Performance.GetBehaviorsByEmployee(employeeId).ToList();
                report.PositiveBehaviors = behaviors.Count(b => b.IsPositive);
                report.NegativeBehaviors = behaviors.Count(b => !b.IsPositive);
                report.RecentBehaviors = behaviors.Take(6).Select(b => ToEntry(b, report.EmployeeName)).ToList();

                if (done.Count == 0)
                {
                    report.HasData = false;
                    report.RecommendationKey = "Perf_Reco_NoData";
                    return report;
                }

                report.HasData = true;
                var trend = done
                    .Select(e => new { e, p = periods.TryGetValue(e.PeriodId, out EvalPeriod pp) ? pp : null })
                    .Where(x => x.p != null)
                    .OrderBy(x => x.p.EndDate)
                    .Select(x => new TrendPoint { PeriodName = x.p.Name, Date = x.p.EndDate, Score = x.e.TotalScore })
                    .ToList();
                report.Trend = trend;

                Evaluation latest = done.First(); // repo returns most-recent period first
                report.LatestScore = latest.TotalScore;
                report.LatestBand = Classify(latest.TotalScore);

                var latestScores = uow.Performance.GetScores(latest.Id)
                    .Select(s => new CriterionScore { Name = s.CriterionName, Score = s.NormalizedScore })
                    .ToList();
                report.Strengths = latestScores.OrderByDescending(s => s.Score).Take(3).ToList();
                report.Weaknesses = latestScores.OrderBy(s => s.Score).Take(3).ToList();

                report.IsDeclining = IsDeclining(trend.Select(t => t.Score).ToList());
                report.RecommendationKey = Recommend(report.LatestBand, report.IsDeclining);
                return report;
            }
        }

        public DeptReport GetDeptReport(long companyId, string department)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                var latest = LatestByEmployee(uow, companyId);
                var employees = uow.Employees.GetByCompany(companyId, includeInactive: false)
                    .Where(e => string.Equals(e.Department ?? string.Empty, department ?? string.Empty,
                                              StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var scored = new List<EmployeeRankRow>();
                foreach (Employee emp in employees)
                {
                    if (!latest.TryGetValue(emp.Id, out Evaluation ev)) continue;
                    scored.Add(new EmployeeRankRow
                    {
                        EmployeeId = emp.Id,
                        EmployeeName = FullName(emp),
                        Department = emp.Department,
                        Score = ev.TotalScore,
                        Band = Classify(ev.TotalScore)
                    });
                }

                var ranked = Rank(scored);
                return new DeptReport
                {
                    Department = department,
                    EmployeeCount = employees.Count,
                    AverageScore = scored.Count == 0 ? 0m : Math.Round(scored.Average(s => s.Score), 1, MidpointRounding.AwayFromZero),
                    Ranking = ranked,
                    NeedSupport = ranked.Where(r => r.Score < SupportBelow).ToList()
                };
            }
        }

        public GeneralReport GetGeneralReport(long companyId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Company company = uow.Companies.GetById(companyId);
                var report = new GeneralReport
                {
                    CompanyId = companyId,
                    CompanyName = company?.NameFr
                };

                var employees = uow.Employees.GetByCompany(companyId, includeInactive: false).ToList();
                report.EmployeeCount = employees.Count;

                var latest = LatestByEmployee(uow, companyId);
                var rows = new List<EmployeeRankRow>();
                foreach (Employee emp in employees)
                {
                    if (!latest.TryGetValue(emp.Id, out Evaluation ev)) continue;
                    rows.Add(new EmployeeRankRow
                    {
                        EmployeeId = emp.Id,
                        EmployeeName = FullName(emp),
                        Department = emp.Department,
                        Score = ev.TotalScore,
                        Band = Classify(ev.TotalScore)
                    });
                }

                report.EvaluatedCount = rows.Count;
                if (rows.Count == 0) { report.HasData = false; return report; }

                report.HasData = true;
                report.CompanyAverage = Math.Round(rows.Average(r => r.Score), 1, MidpointRounding.AwayFromZero);
                report.Departments = rows
                    .GroupBy(r => r.Department ?? string.Empty)
                    .Select(g => new DeptScoreRow
                    {
                        Department = g.Key,
                        EmployeeCount = g.Count(),
                        AverageScore = Math.Round(g.Average(x => x.Score), 1, MidpointRounding.AwayFromZero)
                    })
                    .OrderByDescending(d => d.AverageScore)
                    .ToList();

                var byScore = Rank(rows);
                report.TopPerformers = byScore.Take(5).ToList();
                report.NeedSupport = byScore.Where(r => r.Score < SupportBelow).ToList();
                report.Trend = CompanyTrend(uow, companyId);
                report.BestEmployee = BuildBest(uow, companyId);
                return report;
            }
        }

        public BestEmployeeInfo GetBestEmployee(long companyId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
                return BuildBest(uow, companyId);
        }

        public OverviewData GetOverview(long companyId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                var employees = uow.Employees.GetByCompany(companyId, includeInactive: false).ToList();
                var names = employees.ToDictionary(e => e.Id, FullName);
                var periods = uow.Performance.GetPeriodsByCompany(companyId).ToList();

                // done evaluations per employee, most-recent period first (repo ordering)
                var doneByEmp = uow.Performance.GetEvaluationsByCompany(companyId)
                    .Where(e => e.Status == EvaluationStatus.Done)
                    .GroupBy(e => e.EmployeeId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var data = new OverviewData { EmployeeCount = employees.Count };
                var latest = new List<EmployeeRankRow>();
                var improved = new List<MoverRow>();
                var declined = new List<MoverRow>();

                foreach (Employee emp in employees)
                {
                    if (!doneByEmp.TryGetValue(emp.Id, out var evals) || evals.Count == 0) continue;
                    Evaluation top = evals[0];
                    latest.Add(new EmployeeRankRow
                    {
                        EmployeeId = emp.Id, EmployeeName = FullName(emp), Department = emp.Department,
                        Score = top.TotalScore, Band = Classify(top.TotalScore)
                    });
                    if (evals.Count >= 2)
                    {
                        decimal delta = Math.Round(top.TotalScore - evals[1].TotalScore, 1, MidpointRounding.AwayFromZero);
                        var mover = new MoverRow { EmployeeId = emp.Id, EmployeeName = FullName(emp), Score = top.TotalScore, Delta = delta };
                        if (delta > 0.5m) improved.Add(mover);
                        else if (delta < -0.5m) declined.Add(mover);
                    }
                }

                data.EvaluatedCount = latest.Count;
                data.NotEvaluatedCount = employees.Count - latest.Count;
                if (latest.Count > 0)
                {
                    data.HasData = true;
                    data.CompanyAverage = Math.Round(latest.Average(r => r.Score), 1, MidpointRounding.AwayFromZero);
                    data.CompanyBand = Classify(data.CompanyAverage);
                    data.NeedSupport = latest.Where(r => r.Score < SupportBelow).OrderBy(r => r.Score).ToList();
                    data.BestEmployee = BuildBest(uow, companyId);
                }
                data.Improved = improved.OrderByDescending(m => m.Delta).Take(5).ToList();
                data.Declined = declined.OrderBy(m => m.Delta).Take(5).ToList();

                // the active (most recent open) period — where "start evaluation" acts
                var open = periods.Where(p => p.Status == PeriodStatus.Open).OrderByDescending(p => p.EndDate).ToList();
                EvalPeriod active = open.FirstOrDefault();
                if (active != null) { data.HasActivePeriod = true; data.ActivePeriodId = active.Id; data.ActivePeriodName = active.Name; }

                // actionable alerts: declines to watch + open periods to finish
                var alerts = new List<OverviewAlert>();
                foreach (MoverRow m in data.Declined)
                    alerts.Add(new OverviewAlert { Kind = "decline", EmployeeId = m.EmployeeId, EmployeeName = m.EmployeeName, PeriodId = active?.Id ?? 0, Severity = "danger" });

                int pendingTotal = 0;
                foreach (EvalPeriod p in open)
                {
                    var doneIds = new HashSet<long>(uow.Performance.GetEvaluationsByPeriod(p.Id)
                        .Where(e => e.Status == EvaluationStatus.Done).Select(e => e.EmployeeId));
                    int pending = employees.Count(e => !doneIds.Contains(e.Id));
                    pendingTotal += pending;
                    bool overdue = p.EndDate.Date < DateTime.Today;
                    if (pending > 0 && (overdue || p.EndDate.Date <= DateTime.Today.AddDays(7)))
                        alerts.Add(new OverviewAlert { Kind = overdue ? "overdue" : "pending", PeriodId = p.Id, PeriodName = p.Name, Count = pending, Severity = overdue ? "danger" : "pending" });
                }
                data.PendingCount = pendingTotal;
                data.Alerts = alerts;

                data.RecentActivity = uow.Performance.GetBehaviorsByCompany(companyId).Take(15)
                    .Select(b => ToEntry(b, names.TryGetValue(b.EmployeeId, out string n) ? n : string.Empty)).ToList();
                return data;
            }
        }

        // ===================== CONSUMED BY OTHER MODULES =====================

        public IReadOnlyList<EvaluationSummary> GetByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                Employee emp = uow.Employees.GetById(employeeId);
                string name = emp == null ? string.Empty : FullName(emp);
                var periods = emp == null ? new Dictionary<long, EvalPeriod>()
                    : uow.Performance.GetPeriodsByCompany(emp.CompanyId).ToDictionary(p => p.Id, p => p);

                return uow.Performance.GetEvaluationsByEmployee(employeeId)
                    .Where(e => e.Status == EvaluationStatus.Done)
                    .Select(e =>
                    {
                        periods.TryGetValue(e.PeriodId, out EvalPeriod p);
                        return new EvaluationSummary
                        {
                            EvaluationId = e.Id,
                            EmployeeId = employeeId,
                            EmployeeName = name,
                            Department = e.Department,
                            PeriodId = e.PeriodId,
                            PeriodName = p?.Name,
                            PeriodEnd = p?.EndDate ?? e.EvaluatedDate ?? e.CreatedAtUtc,
                            TotalScore = e.TotalScore,
                            Band = Classify(e.TotalScore),
                            Status = e.Status,
                            EvaluatedDate = e.EvaluatedDate
                        };
                    })
                    .ToList();
            }
        }

        public IReadOnlyList<EvaluationReminder> GetReminders(long companyId, DateTime asOf)
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                var reminders = new List<EvaluationReminder>();
                var openPeriods = uow.Performance.GetPeriodsByCompany(companyId)
                    .Where(p => p.Status == PeriodStatus.Open && p.EndDate <= asOf.AddDays(7))
                    .ToList();
                if (openPeriods.Count == 0) return reminders;

                var employees = uow.Employees.GetByCompany(companyId, includeInactive: false).ToList();
                foreach (EvalPeriod p in openPeriods)
                {
                    var doneIds = new HashSet<long>(uow.Performance.GetEvaluationsByPeriod(p.Id)
                        .Where(e => e.Status == EvaluationStatus.Done).Select(e => e.EmployeeId));
                    foreach (Employee emp in employees)
                    {
                        if (doneIds.Contains(emp.Id)) continue;
                        int daysLeft = (p.EndDate.Date - asOf.Date).Days;
                        reminders.Add(new EvaluationReminder
                        {
                            EmployeeId = emp.Id,
                            EmployeeName = FullName(emp),
                            PeriodName = p.Name,
                            DueDate = p.EndDate,
                            DaysLeft = daysLeft,
                            IsOverdue = daysLeft < 0
                        });
                    }
                }
                return reminders.OrderBy(r => r.DueDate).ToList();
            }
        }

        // ===================== PRIVATE HELPERS ===============================

        /// <summary>The latest completed evaluation per employee for a company.</summary>
        private static Dictionary<long, Evaluation> LatestByEmployee(IUnitOfWork uow, long companyId)
        {
            return uow.Performance.GetEvaluationsByCompany(companyId)
                .Where(e => e.Status == EvaluationStatus.Done)
                .GroupBy(e => e.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First());
        }

        private static IReadOnlyList<EmployeeRankRow> Rank(IEnumerable<EmployeeRankRow> rows)
        {
            var ordered = rows.OrderByDescending(r => r.Score)
                .ThenBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].Rank = i + 1;
            return ordered;
        }

        private static List<TrendPoint> CompanyTrend(IUnitOfWork uow, long companyId)
        {
            var periods = uow.Performance.GetPeriodsByCompany(companyId).ToDictionary(p => p.Id, p => p);
            return uow.Performance.GetEvaluationsByCompany(companyId)
                .Where(e => e.Status == EvaluationStatus.Done && periods.ContainsKey(e.PeriodId))
                .GroupBy(e => e.PeriodId)
                .Select(g => new { p = periods[g.Key], avg = g.Average(x => x.TotalScore) })
                .OrderBy(x => x.p.EndDate)
                .Select(x => new TrendPoint { PeriodName = x.p.Name, Date = x.p.EndDate, Score = Math.Round(x.avg, 1, MidpointRounding.AwayFromZero) })
                .ToList();
        }

        private static BestEmployeeInfo BuildBest(IUnitOfWork uow, long companyId)
        {
            var periods = uow.Performance.GetPeriodsByCompany(companyId).ToDictionary(p => p.Id, p => p);
            var done = uow.Performance.GetEvaluationsByCompany(companyId)
                .Where(e => e.Status == EvaluationStatus.Done && periods.ContainsKey(e.PeriodId))
                .ToList();
            if (done.Count == 0) return null;

            EvalPeriod recent = done.Select(e => periods[e.PeriodId]).OrderByDescending(p => p.EndDate).First();
            Evaluation top = done.Where(e => e.PeriodId == recent.Id).OrderByDescending(e => e.TotalScore).First();
            Employee emp = uow.Employees.GetById(top.EmployeeId);
            if (emp == null) return null;
            return new BestEmployeeInfo
            {
                EmployeeId = emp.Id,
                EmployeeName = FullName(emp),
                Department = emp.Department,
                Score = top.TotalScore,
                PeriodName = recent.Name
            };
        }

        private static bool IsDeclining(IReadOnlyList<decimal> chronologicalScores)
        {
            if (chronologicalScores.Count < 3) return false;
            var last = chronologicalScores.Skip(Math.Max(0, chronologicalScores.Count - 3)).ToList();
            return last[0] > last[1] && last[1] > last[2];
        }

        private static string Recommend(ClassificationBand band, bool declining)
        {
            if (declining) return "Perf_Reco_Watch";
            if (band >= ClassificationBand.VeryGood) return "Perf_Reco_Promotion";
            if (band <= ClassificationBand.Average) return "Perf_Reco_Training";
            return "Perf_Reco_Keep";
        }

        private static decimal Clamp(decimal v) => v < 0m ? 0m : (v > 100m ? 100m : v);

        private static string FullName(Employee e) => ((e.LastNameFr ?? string.Empty) + " " + (e.FirstNameFr ?? string.Empty)).Trim();

        private static string Join(string a, string b)
        {
            a = a ?? string.Empty; b = b ?? string.Empty;
            if (a.Length == 0) return b;
            if (b.Length == 0) return a;
            return a + " — " + b;
        }

        private static BehaviorEntry ToEntry(BehaviorLog b, string employeeName) => new BehaviorEntry
        {
            Id = b.Id,
            EmployeeId = b.EmployeeId,
            EmployeeName = employeeName,
            IsPositive = b.IsPositive,
            Note = b.Note,
            OccurredAt = b.OccurredAt
        };
    }
}
