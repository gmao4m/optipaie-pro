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

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
