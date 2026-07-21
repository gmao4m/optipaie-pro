using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for assets and their assignments. Assets are company-scoped; their
    /// assignments reference the shared Employees table — the employee is never copied.
    /// </summary>
    public interface IAssetRepository
    {
        Asset GetById(long id);

        /// <summary>Assets of a company.</summary>
        IEnumerable<Asset> GetByCompany(long companyId);

        long Insert(Asset asset);

        void Update(Asset asset);

        void SoftDelete(long id);

        // -- assignments -------------------------------------------------------

        AssetAssignment GetAssignmentById(long id);

        /// <summary>The open (not-yet-returned) assignment of an asset, or null.</summary>
        AssetAssignment GetOpenAssignment(long assetId);

        /// <summary>Assignment history of one asset, most recent first.</summary>
        IEnumerable<AssetAssignment> GetAssignmentsByAsset(long assetId);

        /// <summary>Open assignments of one employee (what they currently hold).</summary>
        IEnumerable<AssetAssignment> GetOpenAssignmentsByEmployee(long employeeId);

        long InsertAssignment(AssetAssignment assignment);

        void UpdateAssignment(AssetAssignment assignment);
    }
}
