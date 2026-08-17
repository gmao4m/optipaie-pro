using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>CRUD for the configurable leave-type catalogue (management screen).</summary>
    public interface ILeaveTypeService
    {
        /// <summary>Every type visible to the company (its own + globals), INCLUDING inactive — for management.</summary>
        IReadOnlyList<LeaveTypeDefinition> GetAll(long companyId);

        LeaveTypeDefinition Get(long id);

        Result<long> Save(LeaveTypeDefinition type);

        Result SetActive(long id, bool active);
    }
}
