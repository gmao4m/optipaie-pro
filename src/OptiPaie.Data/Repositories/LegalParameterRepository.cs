using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="LegalParameter"/>.</summary>
    internal sealed class LegalParameterRepository : RepositoryBase, ILegalParameterRepository
    {
        public LegalParameterRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public LegalParameter GetActiveByKey(string key)
        {
            return Connection.QuerySingleOrDefault<LegalParameter>(
                "SELECT * FROM LegalParameters " +
                "WHERE ParamKey = @key AND IsActive = 1 " +
                "ORDER BY EffectiveFrom DESC, Id DESC LIMIT 1;",
                new { key }, Transaction);
        }

        public IEnumerable<LegalParameter> GetAllActive()
        {
            return Connection.Query<LegalParameter>(
                "SELECT * FROM LegalParameters WHERE IsActive = 1 ORDER BY ParamKey;",
                null, Transaction);
        }

        public long Insert(LegalParameter parameter)
        {
            parameter.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO LegalParameters " +
                "(ParamKey, ParamValue, EffectiveFrom, IsActive, Description, CreatedAtUtc) " +
                "VALUES " +
                "(@ParamKey, @ParamValue, @EffectiveFrom, @IsActive, @Description, @CreatedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, parameter, Transaction);
            parameter.Id = id;
            return id;
        }

        public void Update(LegalParameter parameter)
        {
            const string sql =
                "UPDATE LegalParameters SET " +
                "ParamKey = @ParamKey, ParamValue = @ParamValue, EffectiveFrom = @EffectiveFrom, " +
                "IsActive = @IsActive, Description = @Description " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, parameter, Transaction);
        }
    }
}
