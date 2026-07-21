using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A training session organised by a company. Employees are enrolled through
    /// <see cref="TrainingParticipant"/> rows that reference the SHARED Employees table —
    /// no employee data is copied here.
    /// </summary>
    public sealed class TrainingSession : EntityBase
    {
        /// <summary>The organising company.</summary>
        public long CompanyId { get; set; }

        public string Title { get; set; }

        /// <summary>Theme / domain (free text, e.g. "Sécurité", "Bureautique").</summary>
        public string Category { get; set; }

        /// <summary>Training provider / organism.</summary>
        public string Provider { get; set; }

        public TrainingStatus Status { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public string Location { get; set; }

        /// <summary>Total cost of the session (DA).</summary>
        public decimal Cost { get; set; }

        public string Notes { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
