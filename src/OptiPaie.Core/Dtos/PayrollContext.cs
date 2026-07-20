using System.Collections.Generic;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// The complete, self-contained input for one payslip calculation. Built by
    /// services so the engine needs no database or configuration access. Mutable by
    /// design: it is assembled field-by-field before being handed to the engine.
    /// </summary>
    public sealed class PayrollContext
    {
        /// <summary>The payroll period being computed.</summary>
        public PayrollPeriod Period { get; set; }

        /// <summary>The employee's monthly base salary.</summary>
        public decimal BaseSalary { get; set; }

        /// <summary>Days actually worked (for proration of hires/exits/absences).</summary>
        public decimal WorkedDays { get; set; }

        /// <summary>Workable days in the period (the proration denominator).</summary>
        public decimal WorkableDays { get; set; }

        /// <summary>Hours worked (for hour-based elements).</summary>
        public decimal WorkedHours { get; set; }

        /// <summary>The legal/configurable values and rounding policy for this calculation.</summary>
        public LegalSnapshot Legal { get; set; }

        /// <summary>The resolved payroll elements applied this month.</summary>
        public IList<PayrollElementInput> Elements { get; set; } = new List<PayrollElementInput>();
    }
}
