namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// Assignment of a <see cref="PayrollElement"/> to an <see cref="Employee"/>,
    /// with employee-specific values that override the element defaults.
    /// </summary>
    public sealed class EmployeeElement : EntityBase
    {
        /// <summary>Foreign key to the employee.</summary>
        public long EmployeeId { get; set; }

        /// <summary>Foreign key to the payroll element.</summary>
        public long ElementId { get; set; }

        /// <summary>Overriding fixed amount. Null falls back to the element default.</summary>
        public decimal? Amount { get; set; }

        /// <summary>Overriding rate/percentage. Null falls back to the element default.</summary>
        public decimal? Rate { get; set; }

        /// <summary>Overriding quantity. Null falls back to the element default.</summary>
        public decimal? Quantity { get; set; }

        /// <summary>Overriding unit price. Null falls back to the element default.</summary>
        public decimal? UnitPrice { get; set; }

        /// <summary>Whether this assignment is currently active.</summary>
        public bool IsActive { get; set; }
    }
}
