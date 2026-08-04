using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A reusable evaluation grid. Owned by a company, or shipped built-in
    /// (<see cref="CompanyId"/> null, read-only). Its criteria hang off it.
    /// </summary>
    public sealed class EvalTemplate : EntityBase
    {
        /// <summary>Owning company; null = shipped built-in template.</summary>
        public long? CompanyId { get; set; }

        /// <summary>Department this grid targets; null = general / all departments.</summary>
        public string Department { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>Simple (equal criteria) or Weighted (per-criterion percentages).</summary>
        public WeightingMode WeightingMode { get; set; } = WeightingMode.Simple;

        /// <summary>A shipped, read-only starting template.</summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>The company's default grid, offered when none is chosen.</summary>
        public bool IsDefault { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
