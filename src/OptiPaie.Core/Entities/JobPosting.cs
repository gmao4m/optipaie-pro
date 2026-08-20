using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A job opening at a company. Candidates apply to it; hiring one creates a SHARED
    /// employee, so recruitment feeds the rest of the ecosystem (contracts, payroll, …)
    /// without any re-entry.
    /// </summary>
    public sealed class JobPosting : EntityBase
    {
        public long CompanyId { get; set; }

        public string Title { get; set; }

        public string Department { get; set; }

        public string Description { get; set; }

        public JobStatus Status { get; set; }

        public DateTime OpenDate { get; set; }

        /// <summary>Number of positions to fill.</summary>
        public int Positions { get; set; }

        public string Notes { get; set; }

        // -- v1.29 recruitment fields (additive, all optional) ----------------

        /// <summary>Contract type advertised (optional).</summary>
        public ContractType? ContractType { get; set; }

        /// <summary>Application deadline (optional).</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>Person in charge of this recruitment (free text, optional).</summary>
        public string ResponsibleName { get; set; }

        /// <summary>Closure qualification (reserved; e.g. cancellation), with a reason.</summary>
        public int? ClosureType { get; set; }
        public string ClosureReason { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
