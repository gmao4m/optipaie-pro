using System.Collections.Generic;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A request to preview or generate one employee's payslip for a period. Worked
    /// days left at zero means "compute from the employee's hire/exit dates"; a
    /// positive value overrides that.
    /// </summary>
    public sealed class PayrollGenerationRequest
    {
        /// <summary>The company.</summary>
        public long CompanyId { get; set; }

        /// <summary>The employee.</summary>
        public long EmployeeId { get; set; }

        /// <summary>Period year.</summary>
        public int Year { get; set; }

        /// <summary>Period month (1-12).</summary>
        public int Month { get; set; }

        /// <summary>Worked days (0 = derive from hire/exit, or full month).</summary>
        public decimal WorkedDays { get; set; }

        /// <summary>Workable days (0 = derive from the calendar month).</summary>
        public decimal WorkableDays { get; set; }

        /// <summary>Worked hours (for hour-based elements).</summary>
        public decimal WorkedHours { get; set; }

        /// <summary>
        /// Optional base-salary override for this calculation. Null (the default) keeps
        /// the employee's stored base salary, preserving existing behaviour; a value
        /// lets the payroll worksheet edit the "Salaire de base" for this month. It is
        /// an input only — it changes no formula, rate or legal rule.
        /// </summary>
        public decimal? BaseSalaryOverride { get; set; }

        /// <summary>The element entries to apply this month.</summary>
        public IList<PayrollElementEntry> Elements { get; set; } = new List<PayrollElementEntry>();
    }
}
