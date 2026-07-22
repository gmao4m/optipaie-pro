using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// Per-department defaults for a company: which template and which reviewer a new
    /// cycle pre-selects for a department, so launching a cycle needs zero manual matching.
    /// This is the "org structure" the module relies on (department -> default reviewer),
    /// held as data — the app has no login/role layer, so it is a default, not enforcement.
    /// </summary>
    public sealed class PerformanceDeptSetting : EntityBase
    {
        public long CompanyId { get; set; }

        public string Department { get; set; }

        /// <summary>GroupKey of the template pre-selected for this department.</summary>
        public string DefaultTemplateGroupKey { get; set; }

        /// <summary>Employee who reviews this department by default.</summary>
        public long? DefaultReviewerEmployeeId { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
