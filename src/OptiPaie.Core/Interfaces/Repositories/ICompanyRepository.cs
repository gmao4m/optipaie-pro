using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="Company"/>.</summary>
    public interface ICompanyRepository
    {
        /// <summary>Returns the company with the given id, or null.</summary>
        Company GetById(long id);

        /// <summary>Returns all companies, excluding soft-deleted ones unless requested.</summary>
        IEnumerable<Company> GetAll(bool includeDeleted = false);

        /// <summary>Inserts a company and returns its new id.</summary>
        long Insert(Company company);

        /// <summary>Updates an existing company.</summary>
        void Update(Company company);

        /// <summary>Marks a company as deleted (soft delete).</summary>
        void SoftDelete(long id);

        /// <summary>True when a non-deleted company with the given id exists.</summary>
        bool ExistsById(long id);
    }
}
