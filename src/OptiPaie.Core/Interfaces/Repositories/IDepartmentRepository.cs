using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence for company <see cref="Department"/>s.</summary>
    public interface IDepartmentRepository
    {
        IEnumerable<Department> GetByCompany(long companyId);

        Department GetById(long id);

        long Insert(Department department);

        void Update(Department department);
    }
}
