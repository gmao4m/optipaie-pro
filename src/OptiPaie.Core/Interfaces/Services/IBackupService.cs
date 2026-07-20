using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>Database backup and restore operations.</summary>
    public interface IBackupService
    {
        /// <summary>Creates a consistent backup of the database, recording it.</summary>
        Result<BackupRecord> Backup(BackupType type);

        /// <summary>Restores the database from a backup file after verifying it.</summary>
        Result Restore(string backupFilePath);

        /// <summary>Returns the most recent backup records.</summary>
        IReadOnlyList<BackupRecord> GetRecent(int count);
    }
}
