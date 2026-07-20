using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="LegalParameter"/>.</summary>
    public interface ILegalParameterRepository
    {
        /// <summary>Returns the active parameter for a key, or null.</summary>
        LegalParameter GetActiveByKey(string key);

        /// <summary>Returns all active parameters.</summary>
        IEnumerable<LegalParameter> GetAllActive();

        /// <summary>Inserts a parameter and returns its new id.</summary>
        long Insert(LegalParameter parameter);

        /// <summary>Updates an existing parameter.</summary>
        void Update(LegalParameter parameter);
    }
}
