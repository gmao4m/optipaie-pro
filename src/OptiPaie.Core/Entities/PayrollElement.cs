using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A user-creatable payroll element (catalog entry): a gain or a deduction with
    /// a calculation method and its CNAS/IRG/gross treatment. Mirrors the element
    /// definition in the Payroll Engine Specification (§7).
    /// </summary>
    public sealed class PayrollElement : EntityBase
    {
        /// <summary>Element label in French.</summary>
        public string NameFr { get; set; }

        /// <summary>Element label in Arabic.</summary>
        public string NameAr { get; set; }

        /// <summary>Optional internal description.</summary>
        public string Description { get; set; }

        /// <summary>Whether this element is a gain or a deduction.</summary>
        public ElementType ElementType { get; set; }

        /// <summary>How the element's amount is computed.</summary>
        public CalculationMethod CalculationMethod { get; set; }

        /// <summary>Reference base for percentage/rate methods. Null for fixed/quantity methods.</summary>
        public CalculationBase? CalculationBase { get; set; }

        /// <summary>Default fixed amount (FixedAmount method). Null otherwise.</summary>
        public decimal? DefaultAmount { get; set; }

        /// <summary>Default rate/percentage (Percentage or BaseRate methods). Null otherwise.</summary>
        public decimal? DefaultRate { get; set; }

        /// <summary>Default quantity (QuantityUnitPrice / BaseRate). Null otherwise.</summary>
        public decimal? DefaultQuantity { get; set; }

        /// <summary>Default unit price (QuantityUnitPrice method). Null otherwise.</summary>
        public decimal? DefaultUnitPrice { get; set; }

        /// <summary>Periodicity, driving IRG lissage for non-monthly elements.</summary>
        public ElementPeriodicity Periodicity { get; set; }

        /// <summary>Whether the element feeds the contributory base (Base Cotisable).</summary>
        public bool IsCnasApplicable { get; set; }

        /// <summary>Whether the element feeds the taxable base (Base Imposable).</summary>
        public bool IsIrgApplicable { get; set; }

        /// <summary>
        /// Share of the line subject to CNAS, 0..100. Null keeps the legacy
        /// <see cref="IsCnasApplicable"/> yes/no behaviour (100 % or 0 %); a value
        /// (e.g. 50) makes the element partially cotisable. Read by the engine — no
        /// rate is hard-coded.
        /// </summary>
        public decimal? CnasPercent { get; set; }

        /// <summary>
        /// Share of the line subject to IRG, 0..100. Null keeps the legacy
        /// <see cref="IsIrgApplicable"/> yes/no behaviour; a value makes the element
        /// partially taxable.
        /// </summary>
        public decimal? IrgPercent { get; set; }

        /// <summary>Whether the element is counted in the gross salary (Salaire Brut).</summary>
        public bool IsIncludedInGross { get; set; }

        /// <summary>
        /// Optional exemption ceiling. For a non-cotisable/non-taxable element
        /// (e.g. panier, transport), the part above this ceiling becomes cotisable
        /// and taxable. Null means no ceiling.
        /// </summary>
        public decimal? ExemptionCeiling { get; set; }

        /// <summary>Whether the monthly value can be overridden per employee/month.</summary>
        public bool IsEditable { get; set; }

        /// <summary>Whether the element is active (selectable/applied).</summary>
        public bool IsEnabled { get; set; }

        /// <summary>Whether this is a protected built-in element (cannot be deleted).</summary>
        public bool IsSystem { get; set; }

        /// <summary>Position of the element on the payslip.</summary>
        public int DisplayOrder { get; set; }

        /// <summary>Stable internal code (e.g. "PRIME_PANIER"). Optional.</summary>
        public string InternalCode { get; set; }

        /// <summary>Whether the element is printed on the payslip.</summary>
        public bool IsPrintable { get; set; }

        /// <summary>Whether the element participates in IRG lissage (non-monthly amounts).</summary>
        public bool IncludedInLissage { get; set; }

        /// <summary>Whether the element is applied automatically (vs. entered manually each month).</summary>
        public bool IsAutomatic { get; set; }

        /// <summary>UTC timestamp of the last update. Null if never updated.</summary>
        public DateTime? UpdatedAtUtc { get; set; }

        /// <summary>Soft-delete flag.</summary>
        public bool IsDeleted { get; set; }
    }
}
