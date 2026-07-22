using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Orchestrates the whole Performance &amp; Career module: reviews and scoring, the
    /// versioned template library, review cycles, goals, calibration, the company
    /// dashboard, promotions/rewards and employee comparison. Scoring is a weighted
    /// average on each review's configurable scale; rating bands come from the normalised
    /// percentage so reviews on different scales compare cleanly. Attendance is read live
    /// from the Attendance module. Nothing here reads or writes any payroll table.
    /// </summary>
    public sealed class PerformanceService : IPerformanceService
    {
        /// <summary>Default criteria seeded on a blank review (French, /20, equal weight).</summary>
        private static readonly string[] DefaultCriteria =
        {
            "Qualité du travail",
            "Productivité / rendement",
            "Assiduité et ponctualité",
            "Travail d'équipe",
            "Initiative et autonomie"
        };

        private static readonly string[] BandLabels =
        {
            "Insuffisant", "Assez bien", "Bien", "Très bien", "Excellent"
        };

        private const decimal OutlierThreshold = 10m; // percentage points vs company average
        private const int MaxActiveGoals = 5;

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IAttendanceService _attendance;

        public PerformanceService(IUnitOfWorkFactory unitOfWorkFactory, IAttendanceService attendance)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _attendance = attendance; // may enrich a review; never required
        }

        // =====================================================================
        //  Reviews
        // =====================================================================

        public Result<long> CreateDraft(long employeeId, int periodYear, string periodLabel, string reviewer)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (employeeId <= 0 || !uow.Employees.ExistsById(employeeId))
                {
                    return Result.Fail<long>("Employé introuvable.", "Performance_EmployeeNotFound");
                }

                if (periodYear < 2000 || periodYear > 2100)
                {
                    return Result.Fail<long>("Année de période invalide.", "Performance_YearInvalid");
                }

                var review = new PerformanceReview
                {
                    EmployeeId = employeeId,
                    PeriodYear = periodYear,
                    PeriodLabel = string.IsNullOrWhiteSpace(periodLabel) ? periodYear.ToString(CultureInfo.InvariantCulture) : periodLabel,
                    Status = PerformanceStatus.Draft,
                    ReviewDate = DateTime.Today,
                    Reviewer = reviewer,
                    OverallScore = 0m,
                    ScaleMax = 20m
                };

                uow.BeginTransaction();
                try
                {
                    long id = uow.Performance.Insert(review);

                    int order = 0;
                    foreach (string label in DefaultCriteria)
                    {
                        uow.Performance.InsertCriterion(new PerformanceCriterion
                        {
                            ReviewId = id,
                            Label = label,
                            Weight = 1m,
                            Score = 0m,
                            SortOrder = order++
                        });
                    }

                    uow.Commit();
                    return Result.Ok(id);
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result<long> CreateFromTemplate(long employeeId, long templateId, int periodYear, string periodLabel,
            string reviewer, long? reviewerEmployeeId, long? cycleId, DateTime? dueDate, bool selfAssessment)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (employeeId <= 0 || !uow.Employees.ExistsById(employeeId))
                {
                    return Result.Fail<long>("Employé introuvable.", "Performance_EmployeeNotFound");
                }

                PerformanceTemplate template = uow.Performance.GetTemplate(templateId);
                if (template == null)
                {
                    return Result.Fail<long>("Modèle d'évaluation introuvable.", "Performance_TemplateNotFound");
                }

                if (periodYear < 2000 || periodYear > 2100)
                {
                    return Result.Fail<long>("Année de période invalide.", "Performance_YearInvalid");
                }

                List<PerformanceTemplateCriterion> criteria = uow.Performance.GetTemplateCriteria(templateId).ToList();

                uow.BeginTransaction();
                try
                {
                    long id = InsertReviewFromTemplate(uow, employeeId, template, criteria, periodYear,
                        periodLabel, reviewer, reviewerEmployeeId, cycleId, dueDate);
                    uow.Commit();
                    return Result.Ok(id);
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Snapshots a template's criteria into a new draft review, inside the caller's transaction.</summary>
        private static long InsertReviewFromTemplate(IUnitOfWork uow, long employeeId, PerformanceTemplate template,
            IReadOnlyList<PerformanceTemplateCriterion> templateCriteria, int periodYear, string periodLabel,
            string reviewer, long? reviewerEmployeeId, long? cycleId, DateTime? dueDate)
        {
            var review = new PerformanceReview
            {
                EmployeeId = employeeId,
                PeriodYear = periodYear,
                PeriodLabel = string.IsNullOrWhiteSpace(periodLabel) ? periodYear.ToString(CultureInfo.InvariantCulture) : periodLabel,
                Status = PerformanceStatus.Draft,
                ReviewDate = DateTime.Today,
                Reviewer = reviewer,
                OverallScore = 0m,
                CycleId = cycleId,
                TemplateId = template?.Id,
                ReviewerEmployeeId = reviewerEmployeeId,
                DueDate = dueDate,
                ScaleMax = template?.ScaleMax ?? 20m,
                Kind = template?.Kind
            };

            long id = uow.Performance.Insert(review);

            int order = 0;
            foreach (PerformanceTemplateCriterion tc in templateCriteria)
            {
                uow.Performance.InsertCriterion(new PerformanceCriterion
                {
                    ReviewId = id,
                    Label = tc.Label,
                    Weight = tc.WeightPercent,
                    Score = 0m,
                    SortOrder = order++
                });
            }

            return id;
        }

        public Result Save(PerformanceReview review, IEnumerable<PerformanceCriterion> criteria)
        {
            if (review == null)
            {
                return Result.Fail("Aucune évaluation.", "Performance_Required");
            }

            List<PerformanceCriterion> list = (criteria ?? Enumerable.Empty<PerformanceCriterion>()).ToList();

            foreach (PerformanceCriterion c in list)
            {
                if (string.IsNullOrWhiteSpace(c.Label))
                {
                    return Result.Fail("Chaque critère doit avoir un libellé.", "Performance_CriterionLabelRequired");
                }

                if (c.Weight < 0m)
                {
                    return Result.Fail("Le poids d'un critère ne peut pas être négatif.", "Performance_WeightInvalid");
                }
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceReview existing = uow.Performance.GetById(review.Id);
                if (existing == null)
                {
                    return Result.Fail("Évaluation introuvable.", "Performance_NotFound");
                }

                if (existing.Status != PerformanceStatus.Draft)
                {
                    return Result.Fail("Une évaluation finalisée doit d'abord être rouverte.", "Performance_NotDraft");
                }

                decimal scaleMax = existing.ScaleMax <= 0m ? 20m : existing.ScaleMax;
                foreach (PerformanceCriterion c in list)
                {
                    if (c.Score < 0m || c.Score > scaleMax)
                    {
                        return Result.Fail(
                            "Les notes doivent être comprises entre 0 et " + Trim(scaleMax) + ".",
                            "Performance_ScoreRange");
                    }
                }

                existing.PeriodLabel = review.PeriodLabel;
                existing.ReviewDate = review.ReviewDate == default(DateTime) ? existing.ReviewDate : review.ReviewDate;
                existing.Reviewer = review.Reviewer;
                existing.Comments = review.Comments;
                existing.SelfScore = review.SelfScore;
                existing.SelfComments = review.SelfComments;
                existing.OverallScore = ComputeOverall(list);

                uow.BeginTransaction();
                try
                {
                    foreach (PerformanceCriterion old in uow.Performance.GetCriteria(existing.Id).ToList())
                    {
                        uow.Performance.DeleteCriterion(old.Id);
                    }

                    int order = 0;
                    foreach (PerformanceCriterion c in list)
                    {
                        c.ReviewId = existing.Id;
                        c.Id = 0;
                        c.SortOrder = order++;
                        uow.Performance.InsertCriterion(c);
                    }

                    uow.Performance.Update(existing);
                    uow.Commit();
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result Complete(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceReview review = uow.Performance.GetById(id);
                if (review == null)
                {
                    return Result.Fail("Évaluation introuvable.", "Performance_NotFound");
                }

                if (review.Status == PerformanceStatus.Completed)
                {
                    return Result.Ok();
                }

                if (string.IsNullOrWhiteSpace(review.Reviewer))
                {
                    return Result.Fail("Indiquez l'évaluateur avant de finaliser.", "Performance_ReviewerRequired");
                }

                List<PerformanceCriterion> criteria = uow.Performance.GetCriteria(id).ToList();
                if (!criteria.Any(c => c.Weight > 0m))
                {
                    return Result.Fail("Au moins un critère pondéré est requis.", "Performance_NoWeightedCriterion");
                }

                review.Status = PerformanceStatus.Completed;
                review.OverallScore = ComputeOverall(criteria);
                uow.Performance.Update(review);

                if (review.CycleId.HasValue)
                {
                    RefreshCycleStatusInternal(uow, review.CycleId.Value);
                }

                return Result.Ok();
            }
        }

        public Result Reopen(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceReview review = uow.Performance.GetById(id);
                if (review == null)
                {
                    return Result.Fail("Évaluation introuvable.", "Performance_NotFound");
                }

                review.Status = PerformanceStatus.Draft;
                uow.Performance.Update(review);

                if (review.CycleId.HasValue)
                {
                    RefreshCycleStatusInternal(uow, review.CycleId.Value);
                }

                return Result.Ok();
            }
        }

        public Result Delete(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceReview review = uow.Performance.GetById(id);
                uow.Performance.SoftDelete(id);
                if (review != null && review.CycleId.HasValue)
                {
                    RefreshCycleStatusInternal(uow, review.CycleId.Value);
                }
                return Result.Ok();
            }
        }

        public PerformanceReview Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Performance.GetById(id);
            }
        }

        public PerformanceDetail GetDetail(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceReview review = uow.Performance.GetById(id);
                if (review == null) return null;

                var criteria = uow.Performance.GetCriteria(id).ToList();

                return new PerformanceDetail
                {
                    Review = review,
                    Criteria = criteria,
                    Rating = RateScaled(review.OverallScore, review.ScaleMax),
                    Attendance = BuildAttendanceContext(review.EmployeeId, review.PeriodYear)
                };
            }
        }

        public IReadOnlyList<PerformanceSummary> GetByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Performance.GetByEmployee(employeeId)
                    .Select(r => Summarise(uow, r, includeName: false)).ToList();
            }
        }

        public IReadOnlyList<PerformanceSummary> GetByCompanyYear(long companyId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var names = uow.Employees.GetByCompany(companyId)
                    .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());

                var result = new List<PerformanceSummary>();
                foreach (PerformanceReview review in uow.Performance.GetByCompanyYear(companyId, year))
                {
                    PerformanceSummary summary = Summarise(uow, review, includeName: false);
                    names.TryGetValue(review.EmployeeId, out string name);
                    summary.EmployeeName = name;
                    result.Add(summary);
                }

                return result;
            }
        }

        public string Rate(decimal score)
        {
            if (score >= 16m) return "Excellent";
            if (score >= 14m) return "Très bien";
            if (score >= 12m) return "Bien";
            if (score >= 10m) return "Assez bien";
            return "Insuffisant";
        }

        public string RateScaled(decimal score, decimal scaleMax)
        {
            return BandLabels[BandIndex(Percent(score, scaleMax))];
        }

        // =====================================================================
        //  Templates
        // =====================================================================

        public IReadOnlyList<TemplateSummary> GetTemplates(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var result = new List<TemplateSummary>();
                foreach (PerformanceTemplate t in uow.Performance.GetTemplatesForCompany(companyId))
                {
                    int count = uow.Performance.GetTemplateCriteria(t.Id).Count();
                    result.Add(new TemplateSummary
                    {
                        TemplateId = t.Id,
                        GroupKey = t.GroupKey,
                        Name = t.Name,
                        Description = t.Description,
                        Kind = t.Kind,
                        KindLabel = KindLabel(t.Kind),
                        DepartmentTag = t.DepartmentTag,
                        CriteriaCount = count,
                        ScaleMax = t.ScaleMax,
                        IsBuiltIn = t.IsBuiltIn,
                        Version = t.Version
                    });
                }

                return result;
            }
        }

        public TemplateDetail GetTemplateDetail(long templateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceTemplate t = uow.Performance.GetTemplate(templateId);
                if (t == null) return null;

                var criteria = uow.Performance.GetTemplateCriteria(templateId).ToList();
                return new TemplateDetail
                {
                    Template = t,
                    Criteria = criteria,
                    WeightTotal = criteria.Sum(c => c.WeightPercent)
                };
            }
        }

        public Result<long> DuplicateTemplate(long sourceTemplateId, long companyId, string newName)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceTemplate source = uow.Performance.GetTemplate(sourceTemplateId);
                if (source == null)
                {
                    return Result.Fail<long>("Modèle source introuvable.", "Performance_TemplateNotFound");
                }

                var criteria = uow.Performance.GetTemplateCriteria(sourceTemplateId).ToList();

                var copy = new PerformanceTemplate
                {
                    CompanyId = companyId,
                    GroupKey = "tpl-" + Guid.NewGuid().ToString("N"),
                    Version = 1,
                    IsCurrent = true,
                    Kind = source.Kind == TemplateKind.Custom ? TemplateKind.Custom : source.Kind,
                    Name = string.IsNullOrWhiteSpace(newName) ? source.Name + " (copie)" : newName.Trim(),
                    Description = source.Description,
                    DepartmentTag = source.DepartmentTag,
                    ScaleMax = source.ScaleMax,
                    IsBuiltIn = false,
                    IsArchived = false
                };

                uow.BeginTransaction();
                try
                {
                    long id = uow.Performance.InsertTemplate(copy);
                    int order = 0;
                    foreach (PerformanceTemplateCriterion c in criteria)
                    {
                        uow.Performance.InsertTemplateCriterion(new PerformanceTemplateCriterion
                        {
                            TemplateId = id,
                            Label = c.Label,
                            WeightPercent = c.WeightPercent,
                            SortOrder = order++
                        });
                    }

                    uow.Commit();
                    return Result.Ok(id);
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result<long> SaveTemplate(PerformanceTemplate template, IEnumerable<PerformanceTemplateCriterion> criteria)
        {
            if (template == null)
            {
                return Result.Fail<long>("Aucun modèle.", "Performance_Required");
            }

            if (string.IsNullOrWhiteSpace(template.Name))
            {
                return Result.Fail<long>("Le nom du modèle est requis.", "Performance_TemplateNameRequired");
            }

            List<PerformanceTemplateCriterion> list = (criteria ?? Enumerable.Empty<PerformanceTemplateCriterion>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Label)).ToList();

            if (list.Count == 0)
            {
                return Result.Fail<long>("Ajoutez au moins un critère.", "Performance_TemplateNoCriteria");
            }

            decimal weightTotal = list.Sum(c => c.WeightPercent);
            if (Math.Abs(weightTotal - 100m) > 0.5m)
            {
                return Result.Fail<long>(
                    "La somme des poids doit être égale à 100 % (actuellement " + Trim(weightTotal) + " %).",
                    "Performance_TemplateWeightSum");
            }

            if (template.ScaleMax <= 0m) template.ScaleMax = 20m;

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    long templateId;

                    if (template.Id <= 0)
                    {
                        // Brand-new company template.
                        template.CompanyId = template.CompanyId ?? 0;
                        if (string.IsNullOrWhiteSpace(template.GroupKey))
                        {
                            template.GroupKey = "tpl-" + Guid.NewGuid().ToString("N");
                        }
                        template.Version = 1;
                        template.IsCurrent = true;
                        template.IsBuiltIn = false;
                        templateId = uow.Performance.InsertTemplate(template);
                        InsertTemplateCriteria(uow, templateId, list);
                    }
                    else
                    {
                        PerformanceTemplate existing = uow.Performance.GetTemplate(template.Id);
                        if (existing == null)
                        {
                            uow.Rollback();
                            return Result.Fail<long>("Modèle introuvable.", "Performance_TemplateNotFound");
                        }

                        if (existing.IsBuiltIn)
                        {
                            uow.Rollback();
                            return Result.Fail<long>("Un modèle prédéfini ne peut pas être modifié — dupliquez-le d'abord.", "Performance_TemplateBuiltInReadOnly");
                        }

                        if (uow.Performance.IsTemplateGroupUsed(existing.GroupKey))
                        {
                            // Preserve past reviews: create a new version.
                            uow.Performance.SupersedeTemplateGroup(existing.GroupKey, existing.CompanyId);
                            var newVersion = new PerformanceTemplate
                            {
                                CompanyId = existing.CompanyId,
                                GroupKey = existing.GroupKey,
                                Version = existing.Version + 1,
                                IsCurrent = true,
                                Kind = template.Kind,
                                Name = template.Name,
                                Description = template.Description,
                                DepartmentTag = template.DepartmentTag,
                                ScaleMax = template.ScaleMax,
                                IsBuiltIn = false,
                                IsArchived = false
                            };
                            templateId = uow.Performance.InsertTemplate(newVersion);
                            InsertTemplateCriteria(uow, templateId, list);
                        }
                        else
                        {
                            // Not yet used — edit in place.
                            existing.Kind = template.Kind;
                            existing.Name = template.Name;
                            existing.Description = template.Description;
                            existing.DepartmentTag = template.DepartmentTag;
                            existing.ScaleMax = template.ScaleMax;
                            uow.Performance.UpdateTemplate(existing);
                            uow.Performance.DeleteTemplateCriteria(existing.Id);
                            InsertTemplateCriteria(uow, existing.Id, list);
                            templateId = existing.Id;
                        }
                    }

                    uow.Commit();
                    return Result.Ok(templateId);
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result ArchiveTemplate(long templateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceTemplate t = uow.Performance.GetTemplate(templateId);
                if (t == null) return Result.Fail("Modèle introuvable.", "Performance_TemplateNotFound");
                if (t.IsBuiltIn) return Result.Fail("Un modèle prédéfini ne peut pas être archivé.", "Performance_TemplateBuiltInReadOnly");

                t.IsArchived = true;
                uow.Performance.UpdateTemplate(t);
                return Result.Ok();
            }
        }

        public Result DeleteTemplate(long templateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceTemplate t = uow.Performance.GetTemplate(templateId);
                if (t == null) return Result.Ok();
                if (t.IsBuiltIn) return Result.Fail("Un modèle prédéfini ne peut pas être supprimé.", "Performance_TemplateBuiltInReadOnly");

                uow.Performance.SoftDeleteTemplate(templateId);
                return Result.Ok();
            }
        }

        private static void InsertTemplateCriteria(IUnitOfWork uow, long templateId, IReadOnlyList<PerformanceTemplateCriterion> list)
        {
            int order = 0;
            foreach (PerformanceTemplateCriterion c in list)
            {
                uow.Performance.InsertTemplateCriterion(new PerformanceTemplateCriterion
                {
                    TemplateId = templateId,
                    Label = c.Label,
                    WeightPercent = c.WeightPercent,
                    SortOrder = order++
                });
            }
        }

        // =====================================================================
        //  Department defaults
        // =====================================================================

        public IReadOnlyList<PerformanceDeptSetting> GetDeptSettings(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Performance.GetDeptSettings(companyId).ToList();
            }
        }

        public Result SaveDeptSetting(long companyId, string department, string templateGroupKey, long? reviewerEmployeeId)
        {
            if (string.IsNullOrWhiteSpace(department))
            {
                return Result.Fail("Le département est requis.", "Performance_DeptRequired");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceDeptSetting existing = uow.Performance.GetDeptSetting(companyId, department);
                if (existing == null)
                {
                    uow.Performance.InsertDeptSetting(new PerformanceDeptSetting
                    {
                        CompanyId = companyId,
                        Department = department,
                        DefaultTemplateGroupKey = templateGroupKey,
                        DefaultReviewerEmployeeId = reviewerEmployeeId
                    });
                }
                else
                {
                    existing.DefaultTemplateGroupKey = templateGroupKey;
                    existing.DefaultReviewerEmployeeId = reviewerEmployeeId;
                    uow.Performance.UpdateDeptSetting(existing);
                }

                return Result.Ok();
            }
        }

        // =====================================================================
        //  Cycles
        // =====================================================================

        public Result<long> LaunchCycle(CycleLaunchRequest request)
        {
            if (request == null)
            {
                return Result.Fail<long>("Aucune campagne.", "Performance_Required");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Result.Fail<long>("Le nom de la campagne est requis.", "Performance_CycleNameRequired");
            }

            if (request.PeriodYear < 2000 || request.PeriodYear > 2100)
            {
                return Result.Fail<long>("Année de période invalide.", "Performance_YearInvalid");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.Companies.ExistsById(request.CompanyId))
                {
                    return Result.Fail<long>("Société introuvable.", "Performance_CompanyNotFound");
                }

                List<Employee> employees = uow.Employees.GetByCompany(request.CompanyId)
                    .Where(e => !e.IsDeleted && e.IsActive).ToList();

                if (request.EmployeeIds != null && request.EmployeeIds.Count > 0)
                {
                    var wanted = new HashSet<long>(request.EmployeeIds);
                    employees = employees.Where(e => wanted.Contains(e.Id)).ToList();
                }
                else if (request.Departments != null && request.Departments.Count > 0)
                {
                    var depts = new HashSet<string>(request.Departments.Select(Norm));
                    employees = employees.Where(e => depts.Contains(Norm(e.Department))).ToList();
                }

                if (employees.Count == 0)
                {
                    return Result.Fail<long>("Aucun employé à inclure dans la campagne.", "Performance_CycleNoEmployees");
                }

                var byId = employees.ToDictionary(e => e.Id);
                var deptSettings = uow.Performance.GetDeptSettings(request.CompanyId)
                    .GroupBy(d => Norm(d.Department))
                    .ToDictionary(g => g.Key, g => g.First());

                var templateByGroup = new Dictionary<string, PerformanceTemplate>(StringComparer.OrdinalIgnoreCase);
                var criteriaByTemplate = new Dictionary<long, List<PerformanceTemplateCriterion>>();

                PerformanceTemplate ResolveTemplate(string department)
                {
                    string groupKey = null;
                    if (deptSettings.TryGetValue(Norm(department), out PerformanceDeptSetting ds) && !string.IsNullOrWhiteSpace(ds.DefaultTemplateGroupKey))
                    {
                        groupKey = ds.DefaultTemplateGroupKey;
                    }
                    if (string.IsNullOrWhiteSpace(groupKey)) groupKey = request.DefaultTemplateGroupKey;
                    if (string.IsNullOrWhiteSpace(groupKey)) groupKey = "builtin-general";

                    if (!templateByGroup.TryGetValue(groupKey, out PerformanceTemplate tpl))
                    {
                        tpl = uow.Performance.GetCurrentTemplateByGroup(groupKey, request.CompanyId)
                              ?? uow.Performance.GetCurrentTemplateByGroup("builtin-general", request.CompanyId);
                        templateByGroup[groupKey] = tpl;
                        if (tpl != null && !criteriaByTemplate.ContainsKey(tpl.Id))
                        {
                            criteriaByTemplate[tpl.Id] = uow.Performance.GetTemplateCriteria(tpl.Id).ToList();
                        }
                    }
                    return tpl;
                }

                var cycle = new PerformanceCycle
                {
                    CompanyId = request.CompanyId,
                    Name = request.Name.Trim(),
                    CycleType = request.CycleType,
                    StartDate = request.StartDate == default(DateTime) ? DateTime.Today : request.StartDate,
                    EndDate = request.EndDate == default(DateTime) ? DateTime.Today : request.EndDate,
                    Deadline = request.Deadline,
                    Status = PerformanceCycleStatus.Active,
                    SelfAssessment = request.SelfAssessment
                };

                uow.BeginTransaction();
                try
                {
                    long cycleId = uow.Performance.InsertCycle(cycle);

                    foreach (Employee emp in employees)
                    {
                        PerformanceTemplate tpl = ResolveTemplate(emp.Department);
                        List<PerformanceTemplateCriterion> tplCriteria =
                            (tpl != null && criteriaByTemplate.TryGetValue(tpl.Id, out var cc)) ? cc : new List<PerformanceTemplateCriterion>();

                        string reviewerName = null;
                        long? reviewerEmployeeId = null;
                        if (deptSettings.TryGetValue(Norm(emp.Department), out PerformanceDeptSetting ds) && ds.DefaultReviewerEmployeeId.HasValue)
                        {
                            reviewerEmployeeId = ds.DefaultReviewerEmployeeId;
                            if (byId.TryGetValue(ds.DefaultReviewerEmployeeId.Value, out Employee mgr))
                            {
                                reviewerName = (mgr.LastNameFr + " " + mgr.FirstNameFr).Trim();
                            }
                            else
                            {
                                Employee mgrAny = uow.Employees.GetById(ds.DefaultReviewerEmployeeId.Value);
                                if (mgrAny != null) reviewerName = (mgrAny.LastNameFr + " " + mgrAny.FirstNameFr).Trim();
                            }
                        }

                        InsertReviewFromTemplate(uow, emp.Id, tpl, tplCriteria, request.PeriodYear,
                            request.PeriodLabel, reviewerName, reviewerEmployeeId, cycleId, request.Deadline);
                    }

                    uow.Commit();
                    return Result.Ok(cycleId);
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public IReadOnlyList<CycleSummary> GetCycles(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var result = new List<CycleSummary>();
                foreach (PerformanceCycle cycle in uow.Performance.GetCyclesByCompany(companyId))
                {
                    var reviews = uow.Performance.GetByCycle(cycle.Id).ToList();
                    int total = reviews.Count;
                    int completed = reviews.Count(r => r.Status == PerformanceStatus.Completed);
                    result.Add(new CycleSummary
                    {
                        CycleId = cycle.Id,
                        Name = cycle.Name,
                        CycleType = cycle.CycleType,
                        CycleTypeLabel = CycleTypeLabel(cycle.CycleType),
                        StartDate = cycle.StartDate,
                        EndDate = cycle.EndDate,
                        Deadline = cycle.Deadline,
                        Status = cycle.Status,
                        StatusLabel = CycleStatusLabel(cycle.Status),
                        SelfAssessment = cycle.SelfAssessment,
                        TotalReviews = total,
                        CompletedReviews = completed,
                        CompletionPercent = total > 0 ? Math.Round(completed * 100m / total, 0, MidpointRounding.AwayFromZero) : 0m
                    });
                }

                return result;
            }
        }

        public CycleDetail GetCycleDetail(long cycleId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceCycle cycle = uow.Performance.GetCycle(cycleId);
                if (cycle == null) return null;

                var names = uow.Employees.GetByCompany(cycle.CompanyId)
                    .ToDictionary(e => e.Id, e => e);

                var reviews = uow.Performance.GetByCycle(cycleId).ToList();
                DateTime today = DateTime.Today;

                var rows = new List<CycleReviewRow>();
                foreach (PerformanceReview r in reviews)
                {
                    names.TryGetValue(r.EmployeeId, out Employee emp);
                    rows.Add(new CycleReviewRow
                    {
                        ReviewId = r.Id,
                        EmployeeId = r.EmployeeId,
                        EmployeeName = emp != null ? (emp.LastNameFr + " " + emp.FirstNameFr).Trim() : "#" + r.EmployeeId,
                        Department = DeptOf(emp),
                        Reviewer = r.Reviewer,
                        Status = r.Status,
                        StatusLabel = ReviewStatusLabel(r.Status),
                        OverallScore = r.OverallScore,
                        ScaleMax = r.ScaleMax,
                        Rating = RateScaled(r.OverallScore, r.ScaleMax),
                        DueDate = r.DueDate,
                        IsOverdue = r.Status != PerformanceStatus.Completed && r.DueDate.HasValue && r.DueDate.Value.Date < today
                    });
                }

                var byDept = rows
                    .GroupBy(x => x.Department)
                    .Select(g => new DeptCompletionRow
                    {
                        Department = g.Key,
                        Total = g.Count(),
                        Completed = g.Count(x => x.Status == PerformanceStatus.Completed),
                        CompletionPercent = g.Any() ? Math.Round(g.Count(x => x.Status == PerformanceStatus.Completed) * 100m / g.Count(), 0, MidpointRounding.AwayFromZero) : 0m
                    })
                    .OrderBy(x => x.Department)
                    .ToList();

                int total = rows.Count;
                int completed = rows.Count(x => x.Status == PerformanceStatus.Completed);

                return new CycleDetail
                {
                    Cycle = cycle,
                    Reviews = rows,
                    ByDepartment = byDept,
                    Total = total,
                    Completed = completed,
                    CompletionPercent = total > 0 ? Math.Round(completed * 100m / total, 0, MidpointRounding.AwayFromZero) : 0m
                };
            }
        }

        public Result RefreshCycleStatus(long cycleId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                RefreshCycleStatusInternal(uow, cycleId);
                return Result.Ok();
            }
        }

        private static void RefreshCycleStatusInternal(IUnitOfWork uow, long cycleId)
        {
            PerformanceCycle cycle = uow.Performance.GetCycle(cycleId);
            if (cycle == null || cycle.Status == PerformanceCycleStatus.Cancelled) return;

            var reviews = uow.Performance.GetByCycle(cycleId).ToList();
            int total = reviews.Count;
            int completed = reviews.Count(r => r.Status == PerformanceStatus.Completed);

            PerformanceCycleStatus target = (total > 0 && completed == total)
                ? PerformanceCycleStatus.Completed
                : PerformanceCycleStatus.Active;

            if (cycle.Status != target)
            {
                cycle.Status = target;
                uow.Performance.UpdateCycle(cycle);
            }
        }

        public Result CancelCycle(long cycleId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceCycle cycle = uow.Performance.GetCycle(cycleId);
                if (cycle == null) return Result.Fail("Campagne introuvable.", "Performance_CycleNotFound");

                cycle.Status = PerformanceCycleStatus.Cancelled;
                uow.Performance.UpdateCycle(cycle);
                return Result.Ok();
            }
        }

        public Result DeleteCycle(long cycleId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    foreach (PerformanceReview r in uow.Performance.GetByCycle(cycleId).ToList())
                    {
                        uow.Performance.SoftDelete(r.Id);
                    }
                    uow.Performance.SoftDeleteCycle(cycleId);
                    uow.Commit();
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public IReadOnlyList<PerformanceReminder> GetReminders(long companyId, DateTime asOf)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var names = uow.Employees.GetByCompany(companyId)
                    .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());

                DateTime today = asOf.Date;
                var result = new List<PerformanceReminder>();
                foreach (PerformanceReview r in uow.Performance.GetByCompany(companyId))
                {
                    if (r.Status == PerformanceStatus.Completed) continue;
                    if (!r.CycleId.HasValue || !r.DueDate.HasValue) continue;

                    int daysLeft = (r.DueDate.Value.Date - today).Days;
                    names.TryGetValue(r.EmployeeId, out string name);
                    result.Add(new PerformanceReminder
                    {
                        ReviewId = r.Id,
                        CompanyId = companyId,
                        EmployeeName = name,
                        Reviewer = r.Reviewer,
                        DueDate = r.DueDate.Value,
                        DaysLeft = daysLeft,
                        IsOverdue = daysLeft < 0
                    });
                }

                return result.OrderBy(x => x.DueDate).ToList();
            }
        }

        // =====================================================================
        //  Goals
        // =====================================================================

        public IReadOnlyList<GoalRow> GetGoals(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                DateTime today = DateTime.Today;
                return uow.Performance.GetGoalsByEmployee(employeeId).Select(g => ToGoalRow(g, null, today)).ToList();
            }
        }

        public IReadOnlyList<GoalRow> GetCompanyGoals(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var names = uow.Employees.GetByCompany(companyId)
                    .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());
                DateTime today = DateTime.Today;
                var result = new List<GoalRow>();
                foreach (PerformanceGoal g in uow.Performance.GetGoalsByCompany(companyId))
                {
                    names.TryGetValue(g.EmployeeId, out string name);
                    result.Add(ToGoalRow(g, name, today));
                }
                return result;
            }
        }

        public Result<long> CreateGoal(PerformanceGoal goal)
        {
            if (goal == null || string.IsNullOrWhiteSpace(goal.Title))
            {
                return Result.Fail<long>("L'intitulé de l'objectif est requis.", "Performance_GoalTitleRequired");
            }

            if (goal.ProgressPercent < 0m || goal.ProgressPercent > 100m)
            {
                return Result.Fail<long>("La progression doit être comprise entre 0 et 100 %.", "Performance_GoalProgressRange");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (goal.EmployeeId <= 0 || !uow.Employees.ExistsById(goal.EmployeeId))
                {
                    return Result.Fail<long>("Employé introuvable.", "Performance_EmployeeNotFound");
                }

                int active = uow.Performance.GetGoalsByEmployee(goal.EmployeeId).Count(g => g.Status == PerformanceGoalStatus.Active);
                if (goal.Status == PerformanceGoalStatus.Active && active >= MaxActiveGoals)
                {
                    return Result.Fail<long>("Un employé ne peut avoir plus de " + MaxActiveGoals + " objectifs actifs.", "Performance_GoalTooMany");
                }

                long id = uow.Performance.InsertGoal(goal);
                return Result.Ok(id);
            }
        }

        public Result UpdateGoal(PerformanceGoal goal)
        {
            if (goal == null || goal.Id <= 0)
            {
                return Result.Fail("Objectif introuvable.", "Performance_GoalNotFound");
            }

            if (string.IsNullOrWhiteSpace(goal.Title))
            {
                return Result.Fail("L'intitulé de l'objectif est requis.", "Performance_GoalTitleRequired");
            }

            if (goal.ProgressPercent < 0m || goal.ProgressPercent > 100m)
            {
                return Result.Fail("La progression doit être comprise entre 0 et 100 %.", "Performance_GoalProgressRange");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceGoal existing = uow.Performance.GetGoal(goal.Id);
                if (existing == null) return Result.Fail("Objectif introuvable.", "Performance_GoalNotFound");

                existing.Title = goal.Title;
                existing.Description = goal.Description;
                existing.TargetMetric = goal.TargetMetric;
                existing.DueDate = goal.DueDate;
                existing.ProgressPercent = goal.ProgressPercent;
                existing.Status = goal.Status;
                uow.Performance.UpdateGoal(existing);
                return Result.Ok();
            }
        }

        public Result SetGoalProgress(long goalId, decimal progressPercent)
        {
            if (progressPercent < 0m) progressPercent = 0m;
            if (progressPercent > 100m) progressPercent = 100m;

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceGoal g = uow.Performance.GetGoal(goalId);
                if (g == null) return Result.Fail("Objectif introuvable.", "Performance_GoalNotFound");

                g.ProgressPercent = progressPercent;
                if (progressPercent >= 100m && g.Status == PerformanceGoalStatus.Active)
                {
                    g.Status = PerformanceGoalStatus.Achieved;
                }
                uow.Performance.UpdateGoal(g);
                return Result.Ok();
            }
        }

        public Result SetGoalStatus(long goalId, PerformanceGoalStatus status)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PerformanceGoal g = uow.Performance.GetGoal(goalId);
                if (g == null) return Result.Fail("Objectif introuvable.", "Performance_GoalNotFound");

                g.Status = status;
                if (status == PerformanceGoalStatus.Achieved && g.ProgressPercent < 100m) g.ProgressPercent = 100m;
                uow.Performance.UpdateGoal(g);
                return Result.Ok();
            }
        }

        public Result DeleteGoal(long goalId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Performance.SoftDeleteGoal(goalId);
                return Result.Ok();
            }
        }

        public IReadOnlyList<PerformanceGoalTemplate> GetGoalTemplates(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Performance.GetGoalTemplates(companyId).ToList();
            }
        }

        public Result<long> CreateGoalTemplate(PerformanceGoalTemplate template)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.Title))
            {
                return Result.Fail<long>("L'intitulé du modèle d'objectif est requis.", "Performance_GoalTemplateTitleRequired");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                long id = uow.Performance.InsertGoalTemplate(template);
                return Result.Ok(id);
            }
        }

        public Result DeleteGoalTemplate(long templateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Performance.SoftDeleteGoalTemplate(templateId);
                return Result.Ok();
            }
        }

        // =====================================================================
        //  Career events (promotions & rewards)
        // =====================================================================

        public Result<long> LogPromotion(long employeeId, string oldPosition, string newPosition, DateTime date, string reason, long? linkedReviewId)
        {
            if (string.IsNullOrWhiteSpace(newPosition))
            {
                return Result.Fail<long>("Le nouveau poste est requis.", "Performance_PromotionPositionRequired");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.Employees.ExistsById(employeeId))
                {
                    return Result.Fail<long>("Employé introuvable.", "Performance_EmployeeNotFound");
                }

                long id = uow.Performance.InsertCareerEvent(new PerformanceCareerEvent
                {
                    EmployeeId = employeeId,
                    EventType = CareerEventType.Promotion,
                    EventDate = date == default(DateTime) ? DateTime.Today : date,
                    OldPosition = oldPosition,
                    NewPosition = newPosition,
                    Reason = reason,
                    LinkedReviewId = linkedReviewId
                });

                return Result.Ok(id);
            }
        }

        public Result<long> LogReward(long employeeId, decimal amount, string category, DateTime date, string reason)
        {
            if (amount < 0m)
            {
                return Result.Fail<long>("Le montant ne peut pas être négatif.", "Performance_RewardAmountInvalid");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.Employees.ExistsById(employeeId))
                {
                    return Result.Fail<long>("Employé introuvable.", "Performance_EmployeeNotFound");
                }

                long id = uow.Performance.InsertCareerEvent(new PerformanceCareerEvent
                {
                    EmployeeId = employeeId,
                    EventType = CareerEventType.Reward,
                    EventDate = date == default(DateTime) ? DateTime.Today : date,
                    Amount = amount,
                    RewardCategory = category,
                    Reason = reason
                });

                return Result.Ok(id);
            }
        }

        public Result DeleteCareerEvent(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Performance.SoftDeleteCareerEvent(id);
                return Result.Ok();
            }
        }

        public CareerTimeline GetCareerTimeline(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee emp = uow.Employees.GetById(employeeId);
                var items = new List<CareerTimelineItem>();

                foreach (PerformanceReview r in uow.Performance.GetByEmployee(employeeId))
                {
                    items.Add(new CareerTimelineItem
                    {
                        Date = r.ReviewDate,
                        Kind = "review",
                        Title = "Évaluation " + (r.PeriodLabel ?? r.PeriodYear.ToString(CultureInfo.InvariantCulture)),
                        Detail = RateScaled(r.OverallScore, r.ScaleMax) + (string.IsNullOrWhiteSpace(r.Reviewer) ? "" : " — " + r.Reviewer),
                        ValueText = Trim(r.OverallScore) + " / " + Trim(r.ScaleMax),
                        ReferenceId = r.Id
                    });
                }

                foreach (PerformanceGoal g in uow.Performance.GetGoalsByEmployee(employeeId))
                {
                    items.Add(new CareerTimelineItem
                    {
                        Date = g.CreatedAtUtc == default(DateTime) ? (g.DueDate ?? DateTime.Today) : g.CreatedAtUtc,
                        Kind = "goal",
                        Title = "Objectif : " + g.Title,
                        Detail = GoalStatusLabel(g.Status),
                        ValueText = Trim(g.ProgressPercent) + " %",
                        ReferenceId = g.Id
                    });
                }

                foreach (PerformanceCareerEvent ev in uow.Performance.GetCareerEventsByEmployee(employeeId))
                {
                    if (ev.EventType == CareerEventType.Promotion)
                    {
                        items.Add(new CareerTimelineItem
                        {
                            Date = ev.EventDate,
                            Kind = "promotion",
                            Title = "Promotion",
                            Detail = (string.IsNullOrWhiteSpace(ev.OldPosition) ? "" : ev.OldPosition + " → ") + ev.NewPosition,
                            ValueText = ev.Reason,
                            ReferenceId = ev.Id
                        });
                    }
                    else
                    {
                        items.Add(new CareerTimelineItem
                        {
                            Date = ev.EventDate,
                            Kind = "reward",
                            Title = "Récompense" + (string.IsNullOrWhiteSpace(ev.RewardCategory) ? "" : " — " + ev.RewardCategory),
                            Detail = ev.Reason,
                            ValueText = ev.Amount.HasValue ? Trim(ev.Amount.Value) + " DA" : null,
                            ReferenceId = ev.Id
                        });
                    }
                }

                return new CareerTimeline
                {
                    EmployeeId = employeeId,
                    EmployeeName = emp != null ? (emp.LastNameFr + " " + emp.FirstNameFr).Trim() : "#" + employeeId,
                    Items = items.OrderByDescending(i => i.Date).ToList()
                };
            }
        }

        public IReadOnlyList<ContractAmendmentPrompt> GetContractAmendmentPrompts(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var employees = uow.Employees.GetByCompany(companyId).ToDictionary(e => e.Id, e => e);
                var result = new List<ContractAmendmentPrompt>();

                foreach (var grp in uow.Performance.GetCareerEventsByCompany(companyId)
                             .Where(ev => ev.EventType == CareerEventType.Promotion)
                             .GroupBy(ev => ev.EmployeeId))
                {
                    PerformanceCareerEvent latest = grp.OrderByDescending(e => e.EventDate).First();
                    if (!employees.TryGetValue(latest.EmployeeId, out Employee emp)) continue;

                    // An amendment is pending while the employee's position hasn't caught up
                    // to the promoted-to position (activating an amended contract syncs it).
                    if (!string.Equals((emp.Poste ?? string.Empty).Trim(), (latest.NewPosition ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(new ContractAmendmentPrompt
                        {
                            EmployeeId = emp.Id,
                            EmployeeName = (emp.LastNameFr + " " + emp.FirstNameFr).Trim(),
                            OldPosition = latest.OldPosition,
                            NewPosition = latest.NewPosition,
                            Date = latest.EventDate
                        });
                    }
                }

                return result;
            }
        }

        public bool HasProbationReview(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Performance.GetByEmployee(employeeId).Any(r => r.Kind == TemplateKind.Probation);
            }
        }

        // =====================================================================
        //  Calibration / dashboard / comparison
        // =====================================================================

        public CalibrationView GetCalibration(long companyId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var employees = uow.Employees.GetByCompany(companyId).ToDictionary(e => e.Id, e => e);
                var reviews = uow.Performance.GetByCompanyYear(companyId, year)
                    .Where(r => r.Status == PerformanceStatus.Completed).ToList();

                var view = new CalibrationView
                {
                    ScopeLabel = year.ToString(CultureInfo.InvariantCulture),
                    ReviewCount = reviews.Count
                };

                if (reviews.Count == 0)
                {
                    return view;
                }

                var companyDist = new int[5];
                decimal companySum = 0m;
                foreach (PerformanceReview r in reviews)
                {
                    decimal p = Percent(r.OverallScore, r.ScaleMax);
                    companySum += p;
                    companyDist[BandIndex(p)]++;
                }
                view.CompanyAveragePercent = Math.Round(companySum / reviews.Count, 1, MidpointRounding.AwayFromZero);
                view.Distribution = companyDist;

                var deptRows = new List<CalibrationDeptRow>();
                foreach (var grp in reviews.GroupBy(r => DeptOf(employees.TryGetValue(r.EmployeeId, out var e) ? e : null)))
                {
                    var dist = new int[5];
                    decimal sum = 0m;
                    foreach (PerformanceReview r in grp)
                    {
                        decimal p = Percent(r.OverallScore, r.ScaleMax);
                        sum += p;
                        dist[BandIndex(p)]++;
                    }
                    decimal avg = Math.Round(sum / grp.Count(), 1, MidpointRounding.AwayFromZero);
                    decimal delta = Math.Round(avg - view.CompanyAveragePercent, 1, MidpointRounding.AwayFromZero);

                    deptRows.Add(new CalibrationDeptRow
                    {
                        Department = grp.Key,
                        ReviewCount = grp.Count(),
                        AveragePercent = avg,
                        DeltaVsCompany = delta,
                        Distribution = dist,
                        IsOutlierHigh = delta >= OutlierThreshold,
                        IsOutlierLow = delta <= -OutlierThreshold
                    });
                }

                view.Departments = deptRows.OrderByDescending(d => d.AveragePercent).ToList();
                return view;
            }
        }

        public PerformanceDashboard GetDashboard(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var employees = uow.Employees.GetByCompany(companyId).ToDictionary(e => e.Id, e => e);
                var reviews = uow.Performance.GetByCompany(companyId)
                    .Where(r => r.Status == PerformanceStatus.Completed).ToList();

                var dashboard = new PerformanceDashboard { CompanyId = companyId, ReviewCount = reviews.Count };
                if (reviews.Count == 0)
                {
                    return dashboard;
                }

                // Latest completed review per employee = current standing.
                var latestPerEmployee = reviews
                    .GroupBy(r => r.EmployeeId)
                    .Select(g => g.OrderByDescending(r => r.PeriodYear).ThenByDescending(r => r.ReviewDate).ThenByDescending(r => r.Id).First())
                    .ToList();

                var performers = latestPerEmployee.Select(r =>
                {
                    employees.TryGetValue(r.EmployeeId, out Employee e);
                    return new PerformerRow
                    {
                        EmployeeId = r.EmployeeId,
                        EmployeeName = e != null ? (e.LastNameFr + " " + e.FirstNameFr).Trim() : "#" + r.EmployeeId,
                        Department = DeptOf(e),
                        LatestScore = r.OverallScore,
                        ScaleMax = r.ScaleMax,
                        ScorePercent = Percent(r.OverallScore, r.ScaleMax),
                        Rating = RateScaled(r.OverallScore, r.ScaleMax)
                    };
                }).ToList();

                dashboard.CompanyAveragePercent = Math.Round(performers.Average(p => p.ScorePercent), 1, MidpointRounding.AwayFromZero);

                int n = performers.Count;
                int slice = Math.Max(1, (int)Math.Ceiling(n * 0.10));
                dashboard.TopPerformers = performers.OrderByDescending(p => p.ScorePercent).ThenBy(p => p.EmployeeName).Take(slice).ToList();
                dashboard.BottomPerformers = performers.OrderBy(p => p.ScorePercent).ThenBy(p => p.EmployeeName).Take(slice).ToList();

                dashboard.DepartmentAverages = performers
                    .GroupBy(p => p.Department)
                    .Select(g => new DeptScoreRow
                    {
                        Department = g.Key,
                        ReviewCount = g.Count(),
                        AveragePercent = Math.Round(g.Average(p => p.ScorePercent), 1, MidpointRounding.AwayFromZero)
                    })
                    .OrderByDescending(d => d.AveragePercent)
                    .ToList();

                dashboard.Trend = reviews
                    .GroupBy(r => r.PeriodYear)
                    .Select(g => new TrendPoint
                    {
                        Year = g.Key,
                        Label = g.Key.ToString(CultureInfo.InvariantCulture),
                        ReviewCount = g.Count(),
                        AveragePercent = Math.Round(g.Average(r => Percent(r.OverallScore, r.ScaleMax)), 1, MidpointRounding.AwayFromZero)
                    })
                    .OrderBy(t => t.Year)
                    .ToList();

                return dashboard;
            }
        }

        public EmployeeComparison Compare(IReadOnlyList<long> employeeIds)
        {
            var comparison = new EmployeeComparison();
            if (employeeIds == null || employeeIds.Count == 0) return comparison;

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var columns = new List<ComparisonColumn>();
                DateTime today = DateTime.Today;

                foreach (long id in employeeIds.Distinct())
                {
                    Employee e = uow.Employees.GetById(id);
                    if (e == null) continue;

                    var reviews = uow.Performance.GetByEmployee(id)
                        .Where(r => r.Status == PerformanceStatus.Completed).ToList();
                    var history = reviews.Select(r => Summarise(uow, r, includeName: false)).ToList();

                    var goals = uow.Performance.GetGoalsByEmployee(id).ToList();
                    var activeGoals = goals.Where(g => g.Status == PerformanceGoalStatus.Active).ToList();

                    var col = new ComparisonColumn
                    {
                        EmployeeId = id,
                        EmployeeName = (e.LastNameFr + " " + e.FirstNameFr).Trim(),
                        Department = DeptOf(e),
                        Poste = e.Poste,
                        HasReviews = reviews.Count > 0,
                        ReviewCount = reviews.Count,
                        ActiveGoals = activeGoals.Count,
                        GoalCompletionPercent = activeGoals.Count > 0
                            ? Math.Round(activeGoals.Average(g => g.ProgressPercent), 0, MidpointRounding.AwayFromZero)
                            : 0m,
                        History = history
                    };

                    if (reviews.Count > 0)
                    {
                        PerformanceReview latest = reviews
                            .OrderByDescending(r => r.PeriodYear).ThenByDescending(r => r.ReviewDate).ThenByDescending(r => r.Id).First();
                        col.LatestScore = latest.OverallScore;
                        col.ScaleMax = latest.ScaleMax;
                        col.LatestPercent = Percent(latest.OverallScore, latest.ScaleMax);
                        col.Rating = RateScaled(latest.OverallScore, latest.ScaleMax);
                        col.AveragePercent = Math.Round(reviews.Average(r => Percent(r.OverallScore, r.ScaleMax)), 1, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        col.ScaleMax = 20m;
                        col.Rating = "—";
                    }

                    columns.Add(col);
                }

                comparison.Employees = columns;
                return comparison;
            }
        }

        // =====================================================================
        //  Internals
        // =====================================================================

        private static decimal ComputeOverall(IReadOnlyCollection<PerformanceCriterion> criteria)
        {
            decimal totalWeight = criteria.Sum(c => c.Weight);
            if (totalWeight <= 0m) return 0m;

            decimal weighted = criteria.Sum(c => c.Score * c.Weight);
            return Math.Round(weighted / totalWeight, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal Percent(decimal score, decimal scaleMax)
        {
            if (scaleMax <= 0m) return 0m;
            return Math.Round(score / scaleMax * 100m, 1, MidpointRounding.AwayFromZero);
        }

        private static int BandIndex(decimal percent)
        {
            if (percent >= 80m) return 4;
            if (percent >= 70m) return 3;
            if (percent >= 60m) return 2;
            if (percent >= 50m) return 1;
            return 0;
        }

        private PerformanceSummary Summarise(IUnitOfWork uow, PerformanceReview review, bool includeName)
        {
            int count = uow.Performance.GetCriteria(review.Id).Count();

            var summary = new PerformanceSummary
            {
                ReviewId = review.Id,
                EmployeeId = review.EmployeeId,
                PeriodYear = review.PeriodYear,
                PeriodLabel = review.PeriodLabel,
                Status = review.Status,
                ReviewDate = review.ReviewDate,
                Reviewer = review.Reviewer,
                OverallScore = review.OverallScore,
                Rating = RateScaled(review.OverallScore, review.ScaleMax),
                CriteriaCount = count
            };

            if (includeName)
            {
                Employee employee = uow.Employees.GetById(review.EmployeeId);
                if (employee != null)
                {
                    summary.EmployeeName = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
                }
            }

            return summary;
        }

        private GoalRow ToGoalRow(PerformanceGoal g, string employeeName, DateTime today)
        {
            return new GoalRow
            {
                GoalId = g.Id,
                EmployeeId = g.EmployeeId,
                EmployeeName = employeeName,
                Title = g.Title,
                TargetMetric = g.TargetMetric,
                DueDate = g.DueDate,
                ProgressPercent = g.ProgressPercent,
                Status = g.Status,
                StatusLabel = GoalStatusLabel(g.Status),
                IsOverdue = g.Status == PerformanceGoalStatus.Active && g.DueDate.HasValue && g.DueDate.Value.Date < today
            };
        }

        private AttendanceContext BuildAttendanceContext(long employeeId, int year)
        {
            if (_attendance == null) return null;

            int absent = 0, late = 0;
            decimal worked = 0m, overtime = 0m, recorded = 0m;

            for (int month = 1; month <= 12; month++)
            {
                AttendanceSummary summary = _attendance.GetMonthlySummary(employeeId, year, month);
                if (summary == null) continue;

                absent += summary.AbsentDays;
                late += summary.LateCount;
                worked += summary.WorkedHours;
                overtime += summary.OvertimeHours;
                recorded += summary.RecordedDays;
            }

            if (recorded <= 0m) return null;

            return new AttendanceContext
            {
                AbsentDays = absent,
                LateCount = late,
                WorkedHours = worked,
                OvertimeHours = overtime
            };
        }

        private static string DeptOf(Employee e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Department)) return "Non affecté";
            return e.Department.Trim();
        }

        private static string Norm(string s)
        {
            return (s ?? string.Empty).Trim();
        }

        private static string Trim(decimal value)
        {
            decimal rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string KindLabel(TemplateKind kind)
        {
            switch (kind)
            {
                case TemplateKind.General: return "Général";
                case TemplateKind.Sales: return "Commercial";
                case TemplateKind.Production: return "Production";
                case TemplateKind.Administrative: return "Administratif";
                case TemplateKind.Technical: return "Technique / IT";
                case TemplateKind.Management: return "Encadrement";
                case TemplateKind.Probation: return "Période d'essai";
                default: return "Personnalisé";
            }
        }

        private static string CycleTypeLabel(PerformanceCycleType type)
        {
            switch (type)
            {
                case PerformanceCycleType.Quarterly: return "Trimestrielle";
                case PerformanceCycleType.Annual: return "Annuelle";
                case PerformanceCycleType.Probation: return "Fin d'essai";
                default: return "Personnalisée";
            }
        }

        private static string CycleStatusLabel(PerformanceCycleStatus status)
        {
            switch (status)
            {
                case PerformanceCycleStatus.Draft: return "Brouillon";
                case PerformanceCycleStatus.Active: return "En cours";
                case PerformanceCycleStatus.Completed: return "Terminée";
                default: return "Annulée";
            }
        }

        private static string ReviewStatusLabel(PerformanceStatus status)
        {
            return status == PerformanceStatus.Completed ? "Finalisée" : "En cours";
        }

        private static string GoalStatusLabel(PerformanceGoalStatus status)
        {
            switch (status)
            {
                case PerformanceGoalStatus.Achieved: return "Atteint";
                case PerformanceGoalStatus.Missed: return "Manqué";
                case PerformanceGoalStatus.Cancelled: return "Annulé";
                default: return "En cours";
            }
        }
    }
}
