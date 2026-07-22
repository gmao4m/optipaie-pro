using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A company asset (laptop, phone, vehicle, …) that can be handed to employees.
    /// It belongs to a <c>Company</c> and is assigned to a SHARED employee through an
    /// <see cref="AssetAssignment"/> — the employee record is never duplicated here.
    /// </summary>
    public sealed class Asset : EntityBase
    {
        /// <summary>The owning company.</summary>
        public long CompanyId { get; set; }

        public string Name { get; set; }

        public AssetCategory Category { get; set; }

        public AssetStatus Status { get; set; }

        /// <summary>Serial / inventory number.</summary>
        public string SerialNumber { get; set; }

        public DateTime? PurchaseDate { get; set; }

        /// <summary>Acquisition value (DA).</summary>
        public decimal PurchaseValue { get; set; }

        public string Notes { get; set; }

        /// <summary>
        /// Ownership mode. Exclusive (default): one holder at a time — a second assignment
        /// is blocked until the current one is returned. Shared: several employees can hold
        /// it at once, each with an independent assignment, and returning one leaves the
        /// others untouched (e.g. a pool vehicle, a shared tool).
        /// </summary>
        public bool IsShared { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
