using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// An audit-log entry for a database backup, including a checksum and the
    /// schema version captured, so a restore can be verified and matched.
    /// </summary>
    public sealed class BackupRecord : EntityBase
    {
        /// <summary>Full path of the backup file.</summary>
        public string FilePath { get; set; }

        /// <summary>Whether the backup was manual or automatic.</summary>
        public BackupType BackupType { get; set; }

        /// <summary>Size of the backup file in bytes.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Integrity checksum of the backup file.</summary>
        public string Checksum { get; set; }

        /// <summary>Database schema version captured by this backup.</summary>
        public int SchemaVersion { get; set; }
    }
}
