using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for employment contracts. Company-scoped queries join the shared
    /// Employees table — the company is never stored on the contract itself.
    /// </summary>
    public interface IContractRepository
    {
        EmploymentContract GetById(long id);

        /// <summary>Contracts of one employee, most recent first.</summary>
        IEnumerable<EmploymentContract> GetByEmployee(long employeeId);

        /// <summary>The active contract of one employee, or null.</summary>
        EmploymentContract GetActiveForEmployee(long employeeId);

        /// <summary>Contracts of a whole company.</summary>
        IEnumerable<EmploymentContract> GetByCompany(long companyId);

        long Insert(EmploymentContract contract);

        void Update(EmploymentContract contract);

        void SoftDelete(long id);
    }
}
