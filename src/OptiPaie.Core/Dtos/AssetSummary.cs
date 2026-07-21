using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>An asset with its current holder (from the shared employee record).</summary>
    public sealed class AssetSummary
    {
        public long AssetId { get; set; }
        public long CompanyId { get; set; }
        public string Name { get; set; }
        public AssetCategory Category { get; set; }
        public AssetStatus Status { get; set; }
        public string SerialNumber { get; set; }
        public decimal PurchaseValue { get; set; }

        /// <summary>Employee currently holding the asset, or null.</summary>
        public long? HolderId { get; set; }

        /// <summary>Display name of the current holder, or null.</summary>
        public string HolderName { get; set; }

        /// <summary>When the current holder received it, or null.</summary>
        public DateTime? AssignedDate { get; set; }
    }

    /// <summary>An assignment row with the asset and employee names resolved for display.</summary>
    public sealed class AssetAssignmentSummary
    {
        public long AssignmentId { get; set; }
        public long AssetId { get; set; }
        public string AssetName { get; set; }
        public long EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public DateTime AssignedDate { get; set; }
        public DateTime? ReturnedDate { get; set; }
        public string ConditionOut { get; set; }
        public string ConditionIn { get; set; }
        public bool IsOpen { get; set; }
    }
}
