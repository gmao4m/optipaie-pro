using System.Collections.Generic;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// The audit trail. Implements the write-side <see cref="IAuditSink"/> that modules
    /// record through, and adds the read queries used by history tabs and the activity
    /// feed. Read/append only — it never mutates business data or payroll.
    /// </summary>
    public interface IAuditService : IAuditSink
    {
        /// <summary>Full history of one record, most recent first.</summary>
        IReadOnlyList<AuditEntry> GetForEntity(string entityType, long entityId);

        /// <summary>The most recent entries across all modules.</summary>
        IReadOnlyList<AuditEntry> GetRecent(int limit = 20);

        /// <summary>
        /// The most recent entries for ONE company (the activity feed). <paramref name="companyId"/>
        /// is MANDATORY (throws for &lt;= 0). Entries written before the isolation update
        /// (CompanyId NULL) are excluded — never shown, never leaked across companies.
        /// </summary>
        IReadOnlyList<AuditEntry> GetRecentForCompany(long companyId, int limit = 20);
    }
}
