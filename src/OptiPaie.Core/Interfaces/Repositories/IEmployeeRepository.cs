using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="Employee"/>.</summary>
    public interface IEmployeeRepository
    {
        /// <summary>Returns the employee with the given id, or null.</summary>
        Employee GetById(long id);

        /// <summary>Returns the (non-deleted) employees of a company.</summary>
        IEnumerable<Employee> GetByCompany(long companyId, bool includeInactive = true);

        /// <summary>Inserts an employee and returns its new id.</summary>
        long Insert(Employee employee);

        /// <summary>Updates an existing employee.</summary>
        void Update(Employee employee);

        /// <summary>Marks an employee as deleted (soft delete).</summary>
        void SoftDelete(long id);

        /// <summary>True when a non-deleted employee with the given id exists.</summary>
        bool ExistsById(long id);
    }
}
