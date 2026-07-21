using System;
using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Asset operations. Owns the assign/return rules (one holder at a time, full
    /// history) and resolves holders through the shared Employees table.
    /// </summary>
    public interface IAssetService
    {
        Result<long> Save(Asset asset);

        /// <summary>Hands an available asset to an employee.</summary>
        Result Assign(long assetId, long employeeId, DateTime date, string conditionOut, string notes);

        /// <summary>Records the return of the currently held asset.</summary>
        Result Return(long assetId, DateTime date, string conditionIn);

        /// <summary>Marks an asset under repair or retired (must not be assigned).</summary>
        Result SetStatus(long assetId, Core.Enums.AssetStatus status);

        /// <summary>Soft-deletes an asset (must not be currently assigned).</summary>
        Result Delete(long assetId);

        Asset Get(long assetId);

        AssetSummary GetSummary(long assetId);

        /// <summary>Assets of a company with their current holders.</summary>
        IReadOnlyList<AssetSummary> GetByCompany(long companyId);

        /// <summary>Assignment history of one asset.</summary>
        IReadOnlyList<AssetAssignmentSummary> GetHistory(long assetId);

        /// <summary>Assets currently held by one employee.</summary>
        IReadOnlyList<AssetAssignmentSummary> GetHeldByEmployee(long employeeId);
    }
}
