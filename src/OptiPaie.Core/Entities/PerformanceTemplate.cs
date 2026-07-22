using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A reusable, versioned evaluation template — a named set of weighted criteria on a
    /// configurable scale. <see cref="CompanyId"/> null marks a shipped built-in that is
    /// never edited in place; a company copy carries its own <see cref="GroupKey"/>.
    /// Editing a used template creates a new <see cref="Version"/> in the same group so
    /// past reviews keep the criteria they were scored against.
    /// </summary>
    public sealed class PerformanceTemplate : EntityBase
    {
        /// <summary>Owning company, or null for the global built-in library.</summary>
        public long? CompanyId { get; set; }

        /// <summary>Stable key linking every version of the same template.</summary>
        public string GroupKey { get; set; }

        /// <summary>Version number within the group (1-based).</summary>
        public int Version { get; set; } = 1;

        /// <summary>Whether this is the current version of its group.</summary>
        public bool IsCurrent { get; set; } = true;

        public TemplateKind Kind { get; set; } = TemplateKind.Custom;

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>Department this template is the default for (e.g. "Production").</summary>
        public string DepartmentTag { get; set; }

        /// <summary>Top of the rating scale (default 20, the app convention).</summary>
        public decimal ScaleMax { get; set; } = 20m;

        /// <summary>True for the shipped fallback library.</summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>Archived templates stay for history but aren't offered for new cycles.</summary>
        public bool IsArchived { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
