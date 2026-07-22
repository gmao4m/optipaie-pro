using System;
using OptiPaie.Core.Auditing;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One immutable line of the audit trail: what entity changed, how, an optional
    /// old→new value, who did it and when. Append-only — never updated or deleted.
    /// </summary>
    public sealed class AuditEntry : EntityBase
    {
        /// <summary>Logical entity type, e.g. "Leave", "Contract", "Asset".</summary>
        public string EntityType { get; set; }

        /// <summary>The changed record's id.</summary>
        public long EntityId { get; set; }

        public AuditAction Action { get; set; }

        /// <summary>Human-readable French summary of the change.</summary>
        public string Summary { get; set; }

        public string OldValue { get; set; }
        public string NewValue { get; set; }

        /// <summary>
        /// Who performed the action. This is a single-user desktop product, so it is the
        /// local operator name (configurable) rather than an authenticated identity.
        /// </summary>
        public string Actor { get; set; }
    }
}
