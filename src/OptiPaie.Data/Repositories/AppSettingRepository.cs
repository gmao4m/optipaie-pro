using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="AppSetting"/> preferences.</summary>
    internal sealed class AppSettingRepository : RepositoryBase, IAppSettingRepository
    {
        public AppSettingRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public AppSetting Get(string key)
        {
            return Connection.QuerySingleOrDefault<AppSetting>(
                "SELECT * FROM AppSettings WHERE SettingKey = @key;",
                new { key }, Transaction);
        }

        public IEnumerable<AppSetting> GetAll()
        {
            return Connection.Query<AppSetting>("SELECT * FROM AppSettings ORDER BY SettingKey;", null, Transaction);
        }

        public void Upsert(string key, string value)
        {
            DateTime now = DateTime.UtcNow;

            int updated = Connection.Execute(
                "UPDATE AppSettings SET SettingValue = @value, UpdatedAtUtc = @now WHERE SettingKey = @key;",
                new { key, value, now }, Transaction);

            if (updated == 0)
            {
                Connection.Execute(
                    "INSERT INTO AppSettings (SettingKey, SettingValue, CreatedAtUtc) VALUES (@key, @value, @now);",
                    new { key, value, now }, Transaction);
            }
        }
    }
}
