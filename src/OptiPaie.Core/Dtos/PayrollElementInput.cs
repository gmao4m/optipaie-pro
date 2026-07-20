using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A single payroll element resolved for one employee/month, ready for the
    /// engine. Built by services from the element catalog, the employee assignment
    /// and the monthly entry. Mutable by design: it is assembled field-by-field.
    /// </summary>
    public sealed class PayrollElementInput
    {
        /// <summary>Originating element id, for traceability. Null for ad-hoc lines.</summary>
        public long? ElementId { get; set; }

        /// <summary>Line label in French (frozen onto the payslip).</summary>
        public string LabelFr { get; set; }

        /// <summary>Line label in Arabic (frozen onto the payslip).</summary>
        public string LabelAr { get; set; }

        /// <summary>Whether this element is a gain or a deduction.</summary>
        public ElementType ElementType { get; set; }

        /// <summary>How the amount is computed.</summary>
        public CalculationMethod CalculationMethod { get; set; }

        /// <summary>Reference base for percentage/rate methods. Null for fixed/quantity.</summary>
        public CalculationBase? CalculationBase { get; set; }

        /// <summary>Resolved fixed amount (FixedAmount). Null otherwise.</summary>
        public decimal? Amount { get; set; }

        /// <summary>Resolved rate/percentage (Percentage/BaseRate). Null otherwise.</summary>
        public decimal? Rate { get; set; }

        /// <summary>Resolved quantity (QuantityUnitPrice/BaseRate). Null otherwise.</summary>
        public decimal? Quantity { get; set; }

        /// <summary>Resolved unit price (QuantityUnitPrice). Null otherwise.</summary>
        public decimal? UnitPrice { get; set; }

        /// <summary>Whether the element feeds the contributory base (CNAS).</summary>
        public bool IsCnasApplicable { get; set; }

        /// <summary>Whether the element feeds the taxable base (IRG).</summary>
        public bool IsIrgApplicable { get; set; }

        /// <summary>Share of the line that is cotisable (CNAS), 0..1. 1 = fully, 0 = not,
        /// 0.5 = half. Resolved by the service from the element's configuration.</summary>
        public decimal CnasFactor { get; set; } = 1m;

        /// <summary>Share of the line that is taxable (IRG), 0..1.</summary>
        public decimal IrgFactor { get; set; } = 1m;

        /// <summary>Whether the element is counted in the gross salary.</summary>
        public bool IsIncludedInGross { get; set; }

        /// <summary>Optional exemption ceiling; excess becomes cotisable and taxable.</summary>
        public decimal? ExemptionCeiling { get; set; }

        /// <summary>Periodicity, used to decide whether lissage applies.</summary>
        public ElementPeriodicity Periodicity { get; set; }

        /// <summary>Lissage parameters when the element is non-monthly. Null for monthly elements.</summary>
        public LissageInput Lissage { get; set; }

        /// <summary>Position of the line on the payslip.</summary>
        public int DisplayOrder { get; set; }
    }
}
