namespace OptiPaie.Core.Auditing
{
    /// <summary>Category of an audited change.</summary>
    public enum AuditAction
    {
        Created = 1,
        Updated = 2,
        StatusChanged = 3,
        Approved = 4,
        Rejected = 5,
        Deleted = 6,
        Assigned = 7,
        Returned = 8
    }

    /// <summary>
    /// The narrow write-side of the audit log that module services depend on. Kept tiny
    /// and optional so a service can record history without taking a hard dependency —
    /// unset, it is a no-op (<see cref="NullAuditSink"/>), so existing constructions and
    /// tests are unaffected.
    /// </summary>
    public interface IAuditSink
    {
        /// <summary>
        /// Appends one history entry. <paramref name="oldValue"/>/<paramref name="newValue"/>
        /// are optional and used for status/value changes. Best-effort — never throws into
        /// the caller.
        /// </summary>
        void Record(string entityType, long entityId, AuditAction action, string summary,
            string oldValue = null, string newValue = null);
    }

    /// <summary>A no-op sink — the default when auditing is not wired.</summary>
    public sealed class NullAuditSink : IAuditSink
    {
        public static readonly NullAuditSink Instance = new NullAuditSink();
        private NullAuditSink() { }

        public void Record(string entityType, long entityId, AuditAction action, string summary,
            string oldValue = null, string newValue = null)
        {
            // Intentionally does nothing.
        }
    }
}
