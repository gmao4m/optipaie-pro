using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services
{
    /// <summary>
    /// Executive dashboard aggregation. Reads every HR module — never the payroll engine, never a
    /// write — and rolls a company-wide overview: payroll mass, module KPIs, workforce analytics and
    /// the approval/deadline queues. The load path (<see cref="BuildOverview"/>) reads the employee
    /// roster ONCE and uses single SQL aggregates for the module counts (no N+1), so it stays cheap
    /// as the company grows and can run off the UI thread. Everything it returns is language-neutral;
    /// the view-model formats and localizes.
    /// </summary>
    public sealed class DashboardService : IDashboardService
    {
        private readonly ICompanyService _companies;
        private readonly IEmployeeService _employees;
        private readonly IContractService _contracts;
        private readonly ILeaveService _leave;
        private readonly ILoanService _loans;
        private readonly IAttendanceService _attendance;
        private readonly IAtsService _ats;
        private readonly IAssetService _assets;
        private readonly ITrainingService _training;

        public DashboardService(
            ICompanyService companies,
            IEmployeeService employees,
            IContractService contracts,
            ILeaveService leave,
            ILoanService loans,
            IAttendanceService attendance,
            IAtsService ats,
            IAssetService assets,
            ITrainingService training)
        {
            _companies = Guard.AgainstNull(companies, nameof(companies));
            _employees = Guard.AgainstNull(employees, nameof(employees));
            _contracts = Guard.AgainstNull(contracts, nameof(contracts));
            _leave = Guard.AgainstNull(leave, nameof(leave));
            _loans = Guard.AgainstNull(loans, nameof(loans));
            _attendance = Guard.AgainstNull(attendance, nameof(attendance));
            _ats = Guard.AgainstNull(ats, nameof(ats));
            _assets = Guard.AgainstNull(assets, nameof(assets));
            _training = Guard.AgainstNull(training, nameof(training));
        }

        // ------------------------------------------------------------------ snapshot (compat)

        /// <summary>
        /// Legacy company snapshot. Delegates to <see cref="BuildOverview"/> (single computation
        /// path, so the counts never drift) and projects the overlapping fields.
        /// </summary>
        public DashboardSnapshot Build(long companyId, int expiryWindowDays = 30)
        {
            DateTime today = DateTime.Today;
            DashboardOverview o = BuildOverview(companyId, new DateTime(today.Year, today.Month, 1), today, expiryWindowDays);

            return new DashboardSnapshot
            {
                Employees = o.Workforce.Headcount,
                ActiveContracts = o.ActiveContracts,
                ContractsExpiringSoon = o.ContractsExpiringSoon,
                PendingLeave = o.PendingLeave,
                ActiveLoans = o.ActiveLoans,
                LoanOutstanding = o.LoanOutstanding,
                PresentToday = o.PresentToday,
                OnLeaveToday = o.OnLeaveToday,
                OnMissionToday = o.OnMissionToday,
                OpenPostings = o.OpenPostings,
                Candidates = o.Candidates,
                AssetsAssigned = o.AssetsAssigned,
                TrainingUpcoming = o.TrainingUpcoming,
                Deadlines = o.Deadlines.ToList(),
                Approvals = o.Approvals.ToList()
            };
        }

        // ------------------------------------------------------------------ consolidated overview

        public DashboardOverview BuildOverview(long companyId, DateTime periodStart, DateTime periodEnd, int expiryWindowDays = 30)
        {
            RequireCompany(companyId);

            DateTime today = DateTime.Today;
            DateTime start = periodStart.Date;
            DateTime end = periodEnd.Date;

            // ── ONE employee read: the full roster (incl. leavers). "Active" is derived in memory,
            //    so the whole board — headcount, masse, distributions, trend — costs a single query.
            IReadOnlyList<Employee> roster = _employees.GetByCompany(companyId, true);
            List<Employee> active = roster.Where(e => e.IsActive).ToList();

            var o = new DashboardOverview { AsOf = today, PeriodStart = start, PeriodEnd = end };

            // ── payroll (base salary only — never the engine) ──
            decimal masse = active.Sum(e => e.BaseSalary);
            o.MasseSalariale = masse;
            o.SalaireMoyen = active.Count > 0 ? Math.Round(masse / active.Count) : 0m;
            o.MasseTrend = BuildMasseTrend(roster, today);

            // ── workforce analytics (from the already-loaded lists) ──
            o.Workforce = BuildWorkforceCore(active, roster, start, end, today);

            // ── module KPIs via single SQL aggregates (no N+1) ──
            LoanPortfolio loans = _loans.GetActivePortfolio(companyId);
            o.ActiveLoans = loans.ActiveCount;
            o.LoanOutstanding = loans.TotalOutstanding;

            RecruitmentCounts rec = _ats.GetRecruitmentCounts(companyId);
            o.OpenPostings = rec.OpenPostings;
            o.Candidates = rec.Candidates;

            o.AssetsAssigned = _assets.CountAssigned(companyId);
            o.TrainingUpcoming = _training.CountUpcoming(companyId);

            // ── attendance today (one query, counted in memory — bounded by headcount) ──
            foreach (AttendanceRecord a in _attendance.GetCompanyDay(companyId, today))
            {
                switch (a.Status)
                {
                    case AttendanceStatus.Present:
                    case AttendanceStatus.Late:
                        o.PresentToday++;
                        break;
                    case AttendanceStatus.Mission:
                        o.PresentToday++;
                        o.OnMissionToday++;
                        break;
                    case AttendanceStatus.Leave:
                        o.OnLeaveToday++;
                        break;
                }
            }

            // ── contracts: active count + upcoming expiries (fixed window, never overdue) ──
            var deadlines = new List<DeadlineItem>();
            int activeContracts = 0;
            foreach (ContractSummary c in _contracts.GetByCompany(companyId))
            {
                if (c.Status == ContractStatus.Active) activeContracts++;

                if (c.Status == ContractStatus.Active && c.EndDate.HasValue && c.DaysUntilExpiry.HasValue
                    && c.DaysUntilExpiry.Value >= 0 && c.DaysUntilExpiry.Value <= expiryWindowDays)
                {
                    deadlines.Add(new DeadlineItem
                    {
                        Kind = "contract",
                        EmployeeName = c.EmployeeName,
                        Date = c.EndDate.Value,
                        DaysLeft = c.DaysUntilExpiry.Value,
                        ModuleKey = ModuleKeys.Contracts
                    });
                }
            }
            o.ActiveContracts = activeContracts;
            o.ContractsExpiringSoon = deadlines.Count;
            o.Deadlines = deadlines.OrderBy(d => d.Date).Take(20).ToList();

            // ── approvals: pending leave + interviews to schedule ──
            var names = roster.ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());
            var approvals = new List<ApprovalItem>();
            int pendingLeave = 0;
            foreach (LeaveRequest l in _leave.GetByCompanyYear(companyId, today.Year))
            {
                if (l.Status != LeaveStatus.Pending) continue;
                pendingLeave++;
                names.TryGetValue(l.EmployeeId, out string name);
                approvals.Add(new ApprovalItem
                {
                    Kind = "leave",
                    EmployeeName = name,
                    StartDate = l.StartDate,
                    EndDate = l.EndDate,
                    ModuleKey = ModuleKeys.Leave
                });
            }
            o.PendingLeave = pendingLeave;

            foreach (Candidate cand in _ats.GetCandidatesByCompany(companyId))
            {
                if (cand.Stage != CandidateStage.Interview) continue;
                approvals.Add(new ApprovalItem
                {
                    Kind = "recruitment",
                    EmployeeName = (cand.LastName + " " + cand.FirstName).Trim(),
                    ModuleKey = ModuleKeys.Ats
                });
            }
            o.Approvals = approvals.Take(20).ToList();

            return o;
        }

        /// <summary>Base-salary mass over the last 6 months (oldest first), reconstructed from the
        /// roster (incl. leavers) so each month reflects who was actually employed then.</summary>
        private static IReadOnlyList<MonthlyMass> BuildMasseTrend(IReadOnlyList<Employee> roster, DateTime today)
        {
            DateTime firstThisMonth = new DateTime(today.Year, today.Month, 1);
            var trend = new List<MonthlyMass>();
            for (int i = 5; i >= 0; i--)
            {
                DateTime start = firstThisMonth.AddMonths(-i);
                DateTime end = start.AddMonths(1).AddDays(-1);
                decimal amt = roster
                    .Where(e => e.HireDate.Date <= end && (e.ExitDate == null || e.ExitDate.Value.Date >= start))
                    .Sum(e => e.BaseSalary);
                trend.Add(new MonthlyMass { Year = start.Year, Month = start.Month, Amount = amt });
            }
            return trend;
        }

        // ---------------------------------------------------------------- workforce analytics

        private static readonly string[] AgeBandOrder = { "Workforce_Age_U25", "Workforce_Age_2534", "Workforce_Age_3544", "Workforce_Age_4554", "Workforce_Age_55P" };
        private static readonly string[] SeniorityBandOrder = { "Workforce_Sen_U1", "Workforce_Sen_1_3", "Workforce_Sen_3_5", "Workforce_Sen_5_10", "Workforce_Sen_10P" };

        public WorkforceAnalytics BuildWorkforce(long companyId, DateTime periodStart, DateTime periodEnd)
        {
            RequireCompany(companyId);

            DateTime today = DateTime.Today;
            IReadOnlyList<Employee> roster = _employees.GetByCompany(companyId, true);
            List<Employee> active = roster.Where(e => e.IsActive).ToList();
            return BuildWorkforceCore(active, roster, periodStart.Date, periodEnd.Date, today);
        }

        /// <summary>The workforce computation, over pre-loaded lists so the overview never re-queries
        /// employees. State distributions read <paramref name="active"/>; flow figures read the full
        /// <paramref name="roster"/> so a departure in the period is counted.</summary>
        private static WorkforceAnalytics BuildWorkforceCore(
            IReadOnlyList<Employee> active, IReadOnlyList<Employee> roster, DateTime start, DateTime end, DateTime today)
        {
            var wa = new WorkforceAnalytics { AsOf = today, PeriodStart = start, PeriodEnd = end };

            wa.Headcount = active.Count;
            wa.HeadcountIds = active.Select(e => e.Id).ToList();

            var withAge = active.Where(e => e.BirthDate.HasValue).ToList();
            wa.AgeTotalCount = active.Count;
            wa.AgeKnownCount = withAge.Count;
            wa.AverageAge = withAge.Count > 0
                ? (decimal?)Math.Round((decimal)withAge.Average(e => AgeAt(e.BirthDate.Value, today)), 1)
                : null;

            var entries = roster.Where(e => e.HireDate.Date >= start && e.HireDate.Date <= end).ToList();
            var exits = roster.Where(e => e.ExitDate.HasValue && e.ExitDate.Value.Date >= start && e.ExitDate.Value.Date <= end).ToList();
            wa.Entries = entries.Count; wa.EntryIds = entries.Select(e => e.Id).ToList();
            wa.Exits = exits.Count; wa.ExitIds = exits.Select(e => e.Id).ToList();
            wa.HeadcountStart = roster.Count(e => EmployedOn(e, start));
            wa.HeadcountEnd = roster.Count(e => EmployedOn(e, end));
            wa.AverageHeadcount = (wa.HeadcountStart + wa.HeadcountEnd) / 2m;
            wa.TurnoverRate = wa.AverageHeadcount > 0m ? Math.Round(wa.Exits / wa.AverageHeadcount * 100m, 1) : 0m;

            wa.ByContract = KeyedDistribution(active, e => "Enum_ContractType_" + e.ContractType);
            wa.ByMaritalStatus = KeyedDistribution(active, e => "Enum_MaritalStatus_" + e.MaritalStatus);
            wa.ByGender = KeyedDistribution(active, e => "Enum_Gender_" + e.Gender);
            wa.ByCategory = FreeTextDistribution(active, e => e.Category);
            wa.ByDepartment = FreeTextDistribution(active, e => e.Department);
            wa.ByPoste = FreeTextDistribution(active, e => e.Poste);
            wa.ByAgeBand = BandDistribution(active, AgeBandOrder, e => e.BirthDate.HasValue ? AgeBandKey(AgeAt(e.BirthDate.Value, today)) : null);
            wa.BySeniority = BandDistribution(active, SeniorityBandOrder, e => SeniorityBandKey(SeniorityYears(e.HireDate, today)));

            return wa;
        }

        private void RequireCompany(long companyId)
        {
            if (companyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(companyId),
                    "Une société active est obligatoire pour le tableau de bord (jamais « toutes »).");
            }
        }

        private static int AgeAt(DateTime birth, DateTime on)
        {
            int age = on.Year - birth.Year;
            if (birth.Date > on.AddYears(-age)) age--;
            return age < 0 ? 0 : age;
        }

        private static bool EmployedOn(Employee e, DateTime d)
            => e.HireDate.Date <= d && (!e.ExitDate.HasValue || e.ExitDate.Value.Date >= d);

        private static double SeniorityYears(DateTime hire, DateTime on)
            => on.Date <= hire.Date ? 0 : (on.Date - hire.Date).TotalDays / 365.25;

        private static string AgeBandKey(int age)
        {
            if (age < 25) return "Workforce_Age_U25";
            if (age < 35) return "Workforce_Age_2534";
            if (age < 45) return "Workforce_Age_3544";
            if (age < 55) return "Workforce_Age_4554";
            return "Workforce_Age_55P";
        }

        private static string SeniorityBandKey(double years)
        {
            if (years < 1) return "Workforce_Sen_U1";
            if (years < 3) return "Workforce_Sen_1_3";
            if (years < 5) return "Workforce_Sen_3_5";
            if (years < 10) return "Workforce_Sen_5_10";
            return "Workforce_Sen_10P";
        }

        /// <summary>Distribution over a controlled key (enum) — every employee maps to a key, no unknown.</summary>
        private static WorkforceDistribution KeyedDistribution(IReadOnlyList<Employee> emps, Func<Employee, string> key)
        {
            var buckets = emps.GroupBy(key)
                .Select(g => new WorkforceBucket { LabelKey = g.Key, Count = g.Count(), EmployeeIds = g.Select(e => e.Id).ToList() })
                .OrderByDescending(b => b.Count).ToList();
            return new WorkforceDistribution { Buckets = buckets, Total = emps.Count, UnknownCount = 0, Unreliable = false };
        }

        /// <summary>Distribution over a free-text field — grouped case-insensitively and trimmed so
        /// « Cadre » and « cadre » count together; a blank value goes to the « non renseigné » bucket.</summary>
        private static WorkforceDistribution FreeTextDistribution(IReadOnlyList<Employee> emps, Func<Employee, string> field)
        {
            var buckets = emps.Where(e => !string.IsNullOrWhiteSpace(field(e)))
                .GroupBy(e => field(e).Trim().ToLowerInvariant())
                .Select(g => new WorkforceBucket
                {
                    Label = g.Select(e => field(e).Trim()).First(),
                    Count = g.Count(),
                    EmployeeIds = g.Select(e => e.Id).ToList()
                })
                .OrderByDescending(b => b.Count).ToList();

            var unknown = emps.Where(e => string.IsNullOrWhiteSpace(field(e))).ToList();
            if (unknown.Count > 0)
            {
                buckets.Add(new WorkforceBucket
                {
                    LabelKey = "Workforce_Unknown", Count = unknown.Count,
                    EmployeeIds = unknown.Select(e => e.Id).ToList(), IsUnknown = true
                });
            }

            return new WorkforceDistribution
            {
                Buckets = buckets, Total = emps.Count, UnknownCount = unknown.Count,
                Unreliable = unknown.Count > emps.Count / 2
            };
        }

        /// <summary>Distribution over ordered bands (age, seniority); a null band goes to « non renseigné ».
        /// Bands keep their natural order (young→old), not count order.</summary>
        private static WorkforceDistribution BandDistribution(IReadOnlyList<Employee> emps, string[] orderedKeys, Func<Employee, string> bandKey)
        {
            var byKey = emps.Where(e => bandKey(e) != null)
                .GroupBy(bandKey)
                .ToDictionary(g => g.Key, g => g.ToList());

            var buckets = new List<WorkforceBucket>();
            foreach (string k in orderedKeys)
            {
                if (byKey.TryGetValue(k, out var list) && list.Count > 0)
                    buckets.Add(new WorkforceBucket { LabelKey = k, Count = list.Count, EmployeeIds = list.Select(e => e.Id).ToList() });
            }

            var unknown = emps.Where(e => bandKey(e) == null).ToList();
            if (unknown.Count > 0)
            {
                buckets.Add(new WorkforceBucket
                {
                    LabelKey = "Workforce_Unknown", Count = unknown.Count,
                    EmployeeIds = unknown.Select(e => e.Id).ToList(), IsUnknown = true
                });
            }

            return new WorkforceDistribution
            {
                Buckets = buckets, Total = emps.Count, UnknownCount = unknown.Count,
                Unreliable = unknown.Count > emps.Count / 2
            };
        }
    }
}
