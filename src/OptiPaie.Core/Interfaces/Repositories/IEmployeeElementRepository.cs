using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="EmployeeElement"/> assignments.</summary>
    public interface IEmployeeElementRepository
    {
        /// <summary>Returns the assignment with the given id, or null.</summary>
        EmployeeElement GetById(long id);

        /// <summary>Returns the element assignments of an employee.</summary>
        IEnumerable<EmployeeElement> GetByEmployee(long employeeId, bool activeOnly = false);

        /// <summary>Returns the assignment of a specific element to an employee, or null.</summary>
        EmployeeElement GetByEmployeeAndElement(long employeeId, long elementId);

        /// <summary>Inserts an assignment and returns its new id.</summary>
        long Insert(EmployeeElement assignment);

        /// <summary>Updates an existing assignment.</summary>
        void Update(EmployeeElement assignment);

        /// <summary>Permanently removes an assignment.</summary>
        void Delete(long id);
    }
}
