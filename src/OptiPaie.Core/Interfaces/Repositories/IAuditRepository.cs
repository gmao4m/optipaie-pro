using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Append-only persistence for the audit trail.</summary>
    public interface IAuditRepository
    {
        long Insert(AuditEntry entry);

        /// <summary>History of one record, most recent first.</summary>
        IEnumerable<AuditEntry> GetForEntity(string entityType, long entityId);

        /// <summary>The most recent entries across everything (for an activity feed).</summary>
        IEnumerable<AuditEntry> GetRecent(int limit);
    }
}
