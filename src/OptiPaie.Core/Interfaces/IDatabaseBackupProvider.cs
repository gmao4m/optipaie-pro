namespace OptiPaie.Core.Interfaces
{
    /// <summary>
    /// Low-level database backup/restore operations. Implemented in the data layer
    /// (SQLite online-backup API + safe file replacement) and consumed by the
    /// backup service, which adds recording, checksums and pruning.
    /// </summary>
    public interface IDatabaseBackupProvider
    {
        /// <summary>Writes a consistent copy of the live database to the destination file.</summary>
        void Backup(string destinationFilePath);

        /// <summary>True when the file is a valid, integrity-checked SQLite database.</summary>
        bool VerifyDatabaseFile(string filePath);

        /// <summary>Returns the current schema (migration) version of the live database.</summary>
        int GetSchemaVersion();

        /// <summary>Replaces the live database with the given backup file.</summary>
        void RestoreFrom(string backupFilePath);
    }
}
