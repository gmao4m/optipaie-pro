using System;
using System.Collections.Generic;
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
    /// Performance-review orchestration. Owns the scoring (weighted average on a /20
    /// scale) and the rating bands, and pulls the attendance context live from the
    /// Attendance module so a review always reflects the latest presence data — the
    /// figures are read, never copied into the review.
    /// </summary>
    public sealed class PerformanceService : IPerformanceService
    {
        /// <summary>Default criteria seeded on a new review (French, /20, equal weight).</summary>
        private static readonly string[] DefaultCriteria =
        {
            "Qualité du travail",
            "Productivité / rendement",
            "Assiduité et ponctualité",
            "Travail d'équipe",
            "Initiative et autonomie"
        };

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IAttendanceService _attendance;

        public PerformanceService(IUnitOfWorkFactory unitOfWorkFactory, IAttendanceService attendance)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _attendance = attendance; // may be used to enrich a review; never required
        }

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
                    PeriodLabel = string.IsNullOrWhiteSpace(periodLabel) ? periodYear.ToString() : periodLabel,
                    Status = PerformanceStatus.Draft,
                    ReviewDate = DateTime.Today,
                    Reviewer = reviewer,
                    OverallScore = 0m
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

                if (c.Score < 0m || c.Score > 20m)
                {
                    return Result.Fail("Les notes doivent être comprises entre 0 et 20.", "Performance_ScoreRange");
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

                existing.PeriodLabel = review.PeriodLabel;
                existing.ReviewDate = review.ReviewDate == default(DateTime) ? existing.ReviewDate : review.ReviewDate;
                existing.Reviewer = review.Reviewer;
                existing.Comments = review.Comments;
                existing.OverallScore = ComputeOverall(list);

                uow.BeginTransaction();
                try
                {
                    // Criteria are child rows of a draft — replace them wholesale.
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
                return Result.Ok();
            }
        }

        public Result Delete(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Performance.SoftDelete(id);
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
                    Rating = Rate(review.OverallScore),
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

        // -- internals ---------------------------------------------------------

        /// <summary>Weighted average of the criteria on a /20 scale.</summary>
        private static decimal ComputeOverall(IReadOnlyCollection<PerformanceCriterion> criteria)
        {
            decimal totalWeight = criteria.Sum(c => c.Weight);
            if (totalWeight <= 0m) return 0m;

            decimal weighted = criteria.Sum(c => c.Score * c.Weight);
            return Math.Round(weighted / totalWeight, 2, MidpointRounding.AwayFromZero);
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
                Rating = Rate(review.OverallScore),
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

        /// <summary>
        /// Aggregates the employee's attendance across the review year from the shared
        /// Attendance module. Returns null when the module has no data (or is absent),
        /// so the review simply omits the section.
        /// </summary>
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
    }
}
