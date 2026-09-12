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
    /// Executive dashboard aggregation. Reads every HR module through its own service —
    /// no direct SQL, no writes, and never any contact with the payroll engine — and
    /// rolls a company-wide snapshot: KPIs, upcoming deadlines and a single approvals
    /// queue. Iterating the companies is cheap for a desktop install.
    /// </summary>
    public sealed class DashboardService : IDashboardService
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

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

        public DashboardSnapshot Build(long companyId, int expiryWindowDays = 30)
        {
            // A valid active company is MANDATORY — the dashboard is strictly single-company,
            // never an all-companies total (that would leak one client's data into another's).
            if (companyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(companyId),
                    "Une société active est obligatoire pour le tableau de bord (jamais « toutes »).");
            }

            var snapshot = new DashboardSnapshot();
            DateTime today = DateTime.Today;
            int year = today.Year;

            // Employee names (for approval/deadline labels) — from the shared record.
            var names = _employees.GetByCompany(companyId)
                .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());

            snapshot.Employees = _employees.GetByCompany(companyId, false).Count;

            // Contracts.
            foreach (ContractSummary c in _contracts.GetByCompany(companyId))
            {
                if (c.Status == ContractStatus.Active) snapshot.ActiveContracts++;
            }

            foreach (ContractSummary c in _contracts.GetExpiring(companyId, expiryWindowDays))
            {
                snapshot.ContractsExpiringSoon++;
                snapshot.Deadlines.Add(new DeadlineItem
                {
                    Kind = "contract",
                    Title = "Fin de contrat — " + (c.EmployeeName ?? "—"),
                    Detail = "Le " + (c.EndDate.HasValue ? c.EndDate.Value.ToString("dd/MM/yyyy", Fr) : "—") +
                             (c.DaysUntilExpiry.HasValue ? " (" + Countdown(c.DaysUntilExpiry.Value) + ")" : string.Empty),
                    Date = c.EndDate ?? today,
                    DaysLeft = c.DaysUntilExpiry ?? 0,
                    ModuleKey = ModuleKeys.Contracts
                });
            }

            // Leave — pending approvals.
            foreach (LeaveRequest l in _leave.GetByCompanyYear(companyId, year))
            {
                if (l.Status != LeaveStatus.Pending) continue;
                snapshot.PendingLeave++;
                names.TryGetValue(l.EmployeeId, out string name);
                snapshot.Approvals.Add(new ApprovalItem
                {
                    Kind = "leave",
                    Title = "Congé à approuver — " + (name ?? "—"),
                    Detail = l.StartDate.ToString("dd/MM/yyyy", Fr) + " → " + l.EndDate.ToString("dd/MM/yyyy", Fr),
                    ModuleKey = ModuleKeys.Leave
                });
            }

            // Loans.
            foreach (LoanSummary loan in _loans.GetByCompany(companyId))
            {
                if (loan.Status != LoanStatus.Active) continue;
                snapshot.ActiveLoans++;
                snapshot.LoanOutstanding += loan.Outstanding;
            }

            // Attendance — today's live snapshot.
            foreach (AttendanceRecord a in _attendance.GetCompanyDay(companyId, today))
            {
                switch (a.Status)
                {
                    case AttendanceStatus.Present:
                    case AttendanceStatus.Late:
                        snapshot.PresentToday++;
                        break;
                    case AttendanceStatus.Mission:
                        snapshot.PresentToday++;
                        snapshot.OnMissionToday++;
                        break;
                    case AttendanceStatus.Leave:
                        snapshot.OnLeaveToday++;
                        break;
                }
            }

            // Recruitment.
            foreach (JobPostingSummary p in _ats.GetPostingsByCompany(companyId))
            {
                if (p.Status == JobStatus.Open) snapshot.OpenPostings++;
                snapshot.Candidates += p.CandidateCount;
            }

            // Candidates awaiting an interview → "À traiter".
            foreach (Candidate cand in _ats.GetCandidatesByCompany(companyId))
            {
                if (cand.Stage != CandidateStage.Interview) continue;
                snapshot.Approvals.Add(new ApprovalItem
                {
                    Kind = "recruitment",
                    Title = "Entretien à planifier — " + (cand.LastName + " " + cand.FirstName).Trim(),
                    Detail = "Candidat en cours de recrutement",
                    ModuleKey = ModuleKeys.Ats
                });
            }

            // Assets.
            foreach (AssetSummary asset in _assets.GetByCompany(companyId))
            {
                if (asset.Status == AssetStatus.Assigned) snapshot.AssetsAssigned++;
            }

            // Training.
            foreach (TrainingSummary t in _training.GetByCompany(companyId))
            {
                if (t.Status == TrainingStatus.Planned || t.Status == TrainingStatus.Ongoing) snapshot.TrainingUpcoming++;
            }

            snapshot.Deadlines = snapshot.Deadlines.OrderBy(d => d.Date).Take(20).ToList();
            snapshot.Approvals = snapshot.Approvals.Take(20).ToList();
            return snapshot;
        }

        // ---------------------------------------------------------------- workforce analytics

        private static readonly string[] AgeBandOrder = { "Workforce_Age_U25", "Workforce_Age_2534", "Workforce_Age_3544", "Workforce_Age_4554", "Workforce_Age_55P" };
        private static readonly string[] SeniorityBandOrder = { "Workforce_Sen_U1", "Workforce_Sen_1_3", "Workforce_Sen_3_5", "Workforce_Sen_5_10", "Workforce_Sen_10P" };

        public WorkforceAnalytics BuildWorkforce(long companyId, DateTime periodStart, DateTime periodEnd)
        {
            if (companyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(companyId),
                    "Une société active est obligatoire pour le tableau de bord (jamais « toutes »).");
            }

            DateTime today = DateTime.Today;
            DateTime start = periodStart.Date;
            DateTime end = periodEnd.Date;

            // State distributions read the CURRENT effectif (active only); the flow figures read the
            // full roster incl. leavers so a departure in the period is counted. Read-only, no engine.
            IReadOnlyList<Employee> active = _employees.GetByCompany(companyId, false);
            IReadOnlyList<Employee> roster = _employees.GetByCompany(companyId, true);

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

        private static string Countdown(int days)
        {
            if (days < 0) return "en retard de " + (-days) + " j";
            if (days == 0) return "aujourd'hui";
            return "dans " + days + " j";
        }
    }
}
