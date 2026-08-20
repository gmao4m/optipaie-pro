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

        /// <summary>
        /// The company this entry belongs to, resolved from the active company at record time.
        /// NULLABLE: entries written before the multi-company isolation update (migration 0034)
        /// keep NULL and are excluded from every per-company journal — never shown, never leaked
        /// across companies, but kept in the database.
        /// </summary>
        public long? CompanyId { get; set; }

        public AuditAction Action { get; set; }

        /// <summary>Human-readable French summary of the change.</summary>
        public string Summary { get; set; }

        public string OldValue { get; set; }
        public string NewValue { get; set; }

        /// <summary>
        /// Who performed the action — the signed-in principal, resolved at record time: the
        /// local user's username, else the owner account email, else a generic fallback when
        /// no one is signed in (trial / no login gate).
        /// </summary>
        public string Actor { get; set; }
    }
}
