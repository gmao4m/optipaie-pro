using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence for the configurable leave-type catalogue (global + per-company).</summary>
    public interface ILeaveTypeRepository
    {
        LeaveTypeDefinition GetById(long id);

        /// <summary>Every non-deleted type visible to a company: its own plus the global defaults, ordered.</summary>
        IEnumerable<LeaveTypeDefinition> GetForCompany(long companyId);

        long Insert(LeaveTypeDefinition type);

        void Update(LeaveTypeDefinition type);

        void SoftDelete(long id);
    }
}
