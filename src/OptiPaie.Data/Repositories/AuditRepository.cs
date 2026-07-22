using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper persistence for the append-only <see cref="AuditEntry"/> trail.</summary>
    internal sealed class AuditRepository : RepositoryBase, IAuditRepository
    {
        public AuditRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public long Insert(AuditEntry entry)
        {
            entry.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO AuditLog (EntityType, EntityId, Action, Summary, OldValue, NewValue, Actor, CreatedAtUtc) " +
                "VALUES (@EntityType, @EntityId, @Action, @Summary, @OldValue, @NewValue, @Actor, @CreatedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, entry, Transaction);
            entry.Id = id;
            return id;
        }

        public IEnumerable<AuditEntry> GetForEntity(string entityType, long entityId)
        {
            return Connection.Query<AuditEntry>(
                "SELECT * FROM AuditLog WHERE EntityType = @entityType AND EntityId = @entityId " +
                "ORDER BY CreatedAtUtc DESC, Id DESC;",
                new { entityType, entityId }, Transaction);
        }

        public IEnumerable<AuditEntry> GetRecent(int limit)
        {
            return Connection.Query<AuditEntry>(
                "SELECT * FROM AuditLog ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @limit;",
                new { limit }, Transaction);
        }
    }
}
