using System;
using System.Collections.Generic;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A company-wide executive snapshot aggregated across every HR module. Read-only —
    /// it never writes and never touches the payroll engine. Rebuilt on demand.
    /// </summary>
    public sealed class DashboardSnapshot
    {
        public int Companies { get; set; }
        public int Employees { get; set; }

        public int ActiveContracts { get; set; }
        public int ContractsExpiringSoon { get; set; }

        public int PendingLeave { get; set; }

        public int ActiveLoans { get; set; }
        public decimal LoanOutstanding { get; set; }

        public int PresentToday { get; set; }
        public int OnLeaveToday { get; set; }
        public int OnMissionToday { get; set; }

        public int OpenPostings { get; set; }
        public int Candidates { get; set; }

        public int AssetsAssigned { get; set; }
        public int TrainingUpcoming { get; set; }

        /// <summary>Upcoming, dated items across modules (contract expiries, …), soonest first.</summary>
        public List<DeadlineItem> Deadlines { get; set; } = new List<DeadlineItem>();

        /// <summary>Everything waiting for a decision, in one queue.</summary>
        public List<ApprovalItem> Approvals { get; set; } = new List<ApprovalItem>();
    }

    /// <summary>One upcoming, dated item shown in the deadlines widget.</summary>
    public sealed class DeadlineItem
    {
        public string Kind { get; set; }        // e.g. "contract"
        public string Title { get; set; }
        public string Detail { get; set; }
        public DateTime Date { get; set; }
        public int DaysLeft { get; set; }

        /// <summary>Module key to navigate to when clicked.</summary>
        public string ModuleKey { get; set; }
    }

    /// <summary>One item awaiting a decision, shown in the unified approvals queue.</summary>
    public sealed class ApprovalItem
    {
        public string Kind { get; set; }        // e.g. "leave"
        public string Title { get; set; }
        public string Detail { get; set; }
        public string ModuleKey { get; set; }
    }
}
