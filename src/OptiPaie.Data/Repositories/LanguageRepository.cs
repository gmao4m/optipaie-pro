using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based read access for <see cref="Language"/> metadata.</summary>
    internal sealed class LanguageRepository : RepositoryBase, ILanguageRepository
    {
        public LanguageRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public IEnumerable<Language> GetAllEnabled()
        {
            return Connection.Query<Language>(
                "SELECT * FROM Languages WHERE IsEnabled = 1 ORDER BY DisplayOrder;",
                null, Transaction);
        }

        public Language GetByCode(string code)
        {
            return Connection.QuerySingleOrDefault<Language>(
                "SELECT * FROM Languages WHERE Code = @code;",
                new { code }, Transaction);
        }
    }
}
