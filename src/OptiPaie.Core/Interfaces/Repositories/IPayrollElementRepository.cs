using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="PayrollElement"/>.</summary>
    public interface IPayrollElementRepository
    {
        /// <summary>Returns the element with the given id, or null.</summary>
        PayrollElement GetById(long id);

        /// <summary>Returns elements, ordered by display order, with optional filtering.</summary>
        IEnumerable<PayrollElement> GetAll(bool includeDisabled = true, bool includeDeleted = false);

        /// <summary>Inserts an element and returns its new id.</summary>
        long Insert(PayrollElement element);

        /// <summary>Updates an existing element.</summary>
        void Update(PayrollElement element);

        /// <summary>Marks an element as deleted (soft delete).</summary>
        void SoftDelete(long id);

        /// <summary>True when a non-deleted element with the given id exists.</summary>
        bool ExistsById(long id);
    }
}
