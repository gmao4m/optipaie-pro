using System.Collections.Generic;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A monthly entry for one payroll element in a generation request: the element
    /// to apply and the values to use this month (overriding the element/employee
    /// defaults). Lissage parameters are supplied for non-monthly elements.
    /// </summary>
    public sealed class PayrollElementEntry
    {
        /// <summary>The payroll element to apply.</summary>
        public long ElementId { get; set; }

        /// <summary>Fixed amount for this month (FixedAmount method).</summary>
        public decimal? Amount { get; set; }

        /// <summary>Rate/percentage for this month (Percentage/BaseRate).</summary>
        public decimal? Rate { get; set; }

        /// <summary>Quantity for this month (QuantityUnitPrice/BaseRate).</summary>
        public decimal? Quantity { get; set; }

        /// <summary>Unit price for this month (QuantityUnitPrice).</summary>
        public decimal? UnitPrice { get; set; }

        /// <summary>Number of months to spread over for lissage (non-monthly elements).</summary>
        public int? LissageMonths { get; set; }

        /// <summary>The taxable base of each concerned month, for the lissage differential.</summary>
        public IReadOnlyList<decimal> LissageReferenceBases { get; set; }

        /// <summary>
        /// The amount this line evaluates to in the worksheet (Base × Taux, or a direct
        /// value). When set, the engine uses it as the line's amount directly (input
        /// only — it changes no rate, CNAS or IRG rule). Null keeps the element's own
        /// stored calculation method.
        /// </summary>
        public decimal? LineAmount { get; set; }

        /// <summary>True for a free (non-catalog) element created directly on the worksheet.</summary>
        public bool IsManual { get; set; }

        /// <summary>Label of a free element (when <see cref="IsManual"/> is true).</summary>
        public string ManualLabel { get; set; }

        /// <summary>Gain/deduction nature of a free element (when <see cref="IsManual"/> is true).</summary>
        public ElementType ManualType { get; set; }
    }
}
