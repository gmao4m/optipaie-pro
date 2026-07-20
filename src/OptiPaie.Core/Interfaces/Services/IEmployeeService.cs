using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>Application operations for managing employees and their element assignments.</summary>
    public interface IEmployeeService
    {
        /// <summary>Validates and creates an employee, returning its new id.</summary>
        Result<long> Create(Employee employee);

        /// <summary>Validates and updates an employee.</summary>
        Result Update(Employee employee);

        /// <summary>Soft-deletes an employee.</summary>
        Result Delete(long id);

        /// <summary>Returns an employee by id, or null.</summary>
        Employee Get(long id);

        /// <summary>Returns the employees of a company.</summary>
        IReadOnlyList<Employee> GetByCompany(long companyId, bool includeInactive = true);

        /// <summary>Returns the element assignments of an employee.</summary>
        IReadOnlyList<EmployeeElement> GetElements(long employeeId);

        /// <summary>Assigns a payroll element to an employee, returning the assignment id.</summary>
        Result<long> AssignElement(EmployeeElement assignment);

        /// <summary>Updates an existing element assignment.</summary>
        Result UpdateElement(EmployeeElement assignment);

        /// <summary>Removes an element assignment.</summary>
        Result RemoveElement(long employeeElementId);
    }
}
