using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="BackupRecord"/> audit entries.</summary>
    public interface IBackupRecordRepository
    {
        /// <summary>Inserts a backup record and returns its new id.</summary>
        long Insert(BackupRecord record);

        /// <summary>Returns the most recent backup records, newest first.</summary>
        IEnumerable<BackupRecord> GetRecent(int count);
    }
}
