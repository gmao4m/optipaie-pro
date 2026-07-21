using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// An employment contract for one employee. Always references the SHARED
    /// <c>Employees</c> table — no employee or company data is copied. Activating a
    /// contract writes its salary, type and position back onto that shared employee, so
    /// payroll and every other module immediately reflect the terms in force.
    /// </summary>
    public sealed class EmploymentContract : EntityBase
    {
        /// <summary>The shared employee this contract binds.</summary>
        public long EmployeeId { get; set; }

        /// <summary>Contract nature (reuses the payroll contract type: CDI, CDD, …).</summary>
        public ContractType Type { get; set; }

        public ContractStatus Status { get; set; }

        /// <summary>Contract reference / number.</summary>
        public string Reference { get; set; }

        /// <summary>Position held under this contract.</summary>
        public string Position { get; set; }

        /// <summary>Monthly base salary agreed in this contract.</summary>
        public decimal BaseSalary { get; set; }

        public DateTime StartDate { get; set; }

        /// <summary>End date. Null for an open-ended (CDI) contract.</summary>
        public DateTime? EndDate { get; set; }

        /// <summary>Trial period (période d'essai) in days.</summary>
        public int TrialPeriodDays { get; set; }

        /// <summary>The contract this one renews, when applicable.</summary>
        public long? PreviousContractId { get; set; }

        /// <summary>Date the contract was signed.</summary>
        public DateTime? SignedDate { get; set; }

        public string Notes { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
