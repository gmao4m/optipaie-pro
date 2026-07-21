using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One hand-over of an <see cref="Asset"/> to a SHARED employee. An open assignment
    /// (<see cref="ReturnedDate"/> null) means the asset is currently held; closing it
    /// records the return. The full history is kept, so who held what and when is never lost.
    /// </summary>
    public sealed class AssetAssignment : EntityBase
    {
        public long AssetId { get; set; }

        /// <summary>The shared employee holding the asset.</summary>
        public long EmployeeId { get; set; }

        public DateTime AssignedDate { get; set; }

        /// <summary>Return date. Null while the asset is still held.</summary>
        public DateTime? ReturnedDate { get; set; }

        /// <summary>Condition noted at hand-over.</summary>
        public string ConditionOut { get; set; }

        /// <summary>Condition noted at return.</summary>
        public string ConditionIn { get; set; }

        public string Notes { get; set; }

        public bool IsDeleted { get; set; }
    }
}
