using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>Application operations for managing companies.</summary>
    public interface ICompanyService
    {
        /// <summary>Validates and creates a company, returning its new id.</summary>
        Result<long> Create(Company company);

        /// <summary>Validates and updates a company.</summary>
        Result Update(Company company);

        /// <summary>Soft-deletes a company.</summary>
        Result Delete(long id);

        /// <summary>Returns a company by id, or null.</summary>
        Company Get(long id);

        /// <summary>Returns all non-deleted companies.</summary>
        IReadOnlyList<Company> GetAll();
    }
}
