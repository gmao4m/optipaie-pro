using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Per-company department management. Departments are the backbone of the
    /// Performance module (evaluation criteria hang off them) and the source list for
    /// the employee Department dropdown. The employee's stored Department value stays a
    /// plain name string, so this list only curates what is offered — it never breaks
    /// existing department-driven features (attendance grouping, reports, ATS).
    /// </summary>
    public interface IDepartmentService
    {
        /// <summary>
        /// Active departments for a company. On first use for a company with none,
        /// lazily seeds a sensible default set plus any department names already present
        /// on that company's employees, so the list is never empty out of the box.
        /// </summary>
        IReadOnlyList<Department> GetForCompany(long companyId);

        /// <summary>Just the department names for a company (dropdown source).</summary>
        IReadOnlyList<string> GetNamesForCompany(long companyId);

        /// <summary>Adds or updates a department (name required, unique per company).</summary>
        Result<long> Save(Department department);

        /// <summary>Soft-deletes a department. Employees keep their stored name value.</summary>
        Result Remove(long departmentId);
    }
}
