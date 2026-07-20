using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>Application operations for managing the payroll element catalog.</summary>
    public interface IPayrollElementService
    {
        /// <summary>Validates and creates an element, returning its new id.</summary>
        Result<long> Create(PayrollElement element);

        /// <summary>Validates and updates an element.</summary>
        Result Update(PayrollElement element);

        /// <summary>Soft-deletes an element (system elements cannot be deleted).</summary>
        Result Delete(long id);

        /// <summary>Returns an element by id, or null.</summary>
        PayrollElement Get(long id);

        /// <summary>Returns the elements, ordered by display order.</summary>
        IReadOnlyList<PayrollElement> GetAll(bool includeDisabled = true);
    }
}
