namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A department (قسم) of a company — Production, Commercial, Administration, … The
    /// employee's <see cref="Employee.Department"/> stays the department NAME (so every
    /// existing feature that groups by that string keeps working); this managed list is
    /// what the employee edit form offers as a dropdown, and what the evaluation module
    /// hangs its per-department criteria grids off.
    /// </summary>
    public sealed class Department : EntityBase
    {
        public long CompanyId { get; set; }

        /// <summary>Department name (also the value stored on the employee).</summary>
        public string Name { get; set; }

        /// <summary>Ordering in the list (lower first).</summary>
        public int DisplayOrder { get; set; }

        public System.DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
