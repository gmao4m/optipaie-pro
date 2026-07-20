using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="BackupRecord"/> entries.</summary>
    internal sealed class BackupRecordRepository : RepositoryBase, IBackupRecordRepository
    {
        public BackupRecordRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public long Insert(BackupRecord record)
        {
            record.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO BackupRecords " +
                "(FilePath, BackupType, SizeBytes, Checksum, SchemaVersion, CreatedAtUtc) " +
                "VALUES " +
                "(@FilePath, @BackupType, @SizeBytes, @Checksum, @SchemaVersion, @CreatedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, record, Transaction);
            record.Id = id;
            return id;
        }

        public IEnumerable<BackupRecord> GetRecent(int count)
        {
            return Connection.Query<BackupRecord>(
                "SELECT * FROM BackupRecords ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @count;",
                new { count }, Transaction);
        }
    }
}
