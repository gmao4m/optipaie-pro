using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A contract with its derived position (days until expiry, expiry flags). The
    /// employee name comes from the shared employee record, never copied.
    /// </summary>
    public sealed class ContractSummary
    {
        public long ContractId { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }

        public ContractType Type { get; set; }
        public ContractStatus Status { get; set; }
        public string Reference { get; set; }
        public string Position { get; set; }
        public decimal BaseSalary { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        /// <summary>Days from today to the end date (negative when overdue); null for a CDI.</summary>
        public int? DaysUntilExpiry { get; set; }

        /// <summary>Active fixed-term contract whose end date has already passed.</summary>
        public bool IsOverdue { get; set; }

        /// <summary>Active fixed-term contract ending within the alert window.</summary>
        public bool IsExpiringSoon { get; set; }
    }
}
