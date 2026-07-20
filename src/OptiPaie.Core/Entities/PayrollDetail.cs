using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// One line of a <see cref="Payslip"/>. Labels and computed values are frozen
    /// at generation so renaming or editing an element later never alters a past
    /// payslip.
    /// </summary>
    public sealed class PayrollDetail : EntityBase
    {
        /// <summary>Foreign key to the parent payslip.</summary>
        public long PayslipId { get; set; }

        /// <summary>
        /// Foreign key to the originating payroll element, for reference only.
        /// Null for purely statutory lines (e.g. CNAS, IRG) that have no catalog element.
        /// </summary>
        public long? ElementId { get; set; }

        /// <summary>Frozen line label in French.</summary>
        public string LabelFr { get; set; }

        /// <summary>Frozen line label in Arabic.</summary>
        public string LabelAr { get; set; }

        /// <summary>Whether this line is a gain or a deduction.</summary>
        public ElementType ElementType { get; set; }

        /// <summary>Base used for the line, when applicable. Null otherwise.</summary>
        public decimal? Base { get; set; }

        /// <summary>Rate/percentage used, when applicable. Null otherwise.</summary>
        public decimal? Rate { get; set; }

        /// <summary>Quantity used, when applicable. Null otherwise.</summary>
        public decimal? Quantity { get; set; }

        /// <summary>Unit price used, when applicable. Null otherwise.</summary>
        public decimal? UnitPrice { get; set; }

        /// <summary>Computed amount of the line.</summary>
        public decimal Amount { get; set; }

        /// <summary>Whether the line contributed to the contributory base.</summary>
        public bool IsCnasApplicable { get; set; }

        /// <summary>Whether the line contributed to the taxable base.</summary>
        public bool IsIrgApplicable { get; set; }

        /// <summary>Position of the line on the payslip.</summary>
        public int DisplayOrder { get; set; }
    }
}
