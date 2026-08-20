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

        private static string Countdown(int days)
        {
            if (days < 0) return "en retard de " + (-days) + " j";
            if (days == 0) return "aujourd'hui";
            return "dans " + days + " j";
        }
    }
}
