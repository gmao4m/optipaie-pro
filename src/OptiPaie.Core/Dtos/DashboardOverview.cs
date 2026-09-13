using System;
using System.Collections.Generic;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// The complete dashboard payload for one company, built in a SINGLE service call so the
    /// view-model issues one off-thread request instead of a dozen. Everything is language-neutral
    /// (raw numbers, dates, employee names and localization keys); the view-model formats and
    /// localizes. Read-only aggregation — the payroll engine is never involved.
    /// </summary>
    public sealed class DashboardOverview
    {
        public DateTime AsOf { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        // ── payroll (base salary mass, never the engine) ──
        public decimal MasseSalariale { get; set; }
        public decimal SalaireMoyen { get; set; }

        /// <summary>Reconstructed base-salary mass for each of the last 6 months (oldest first).</summary>
        public IReadOnlyList<MonthlyMass> MasseTrend { get; set; } = new List<MonthlyMass>();

        // ── module KPIs ──
        public int ActiveContracts { get; set; }
        public int ContractsExpiringSoon { get; set; }
        public int PendingLeave { get; set; }
        public int ActiveLoans { get; set; }
        public decimal LoanOutstanding { get; set; }
        public int PresentToday { get; set; }
        public int OnLeaveToday { get; set; }
        public int OnMissionToday { get; set; }

        /// <summary>True when at least one attendance record exists for today — so the UI can tell a
        /// real "0 present" (a day off / nobody in) from "no attendance entered yet".</summary>
        public bool AttendanceRecordedToday { get; set; }
        public int OpenPostings { get; set; }
        public int Candidates { get; set; }
        public int AssetsAssigned { get; set; }
        public int TrainingUpcoming { get; set; }

        /// <summary>Workforce (effectif) analytics — headcount, age, flow figures and every distribution.</summary>
        public WorkforceAnalytics Workforce { get; set; } = new WorkforceAnalytics();

        /// <summary>Everything waiting for a decision (leave to approve, interviews to schedule).</summary>
        public IReadOnlyList<ApprovalItem> Approvals { get; set; } = new List<ApprovalItem>();

        /// <summary>Upcoming, dated items (contract expiries within the window), soonest first.</summary>
        public IReadOnlyList<DeadlineItem> Deadlines { get; set; } = new List<DeadlineItem>();
    }

    /// <summary>Base-salary mass reconstructed for one calendar month.</summary>
    public sealed class MonthlyMass
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Amount { get; set; }
    }
}
