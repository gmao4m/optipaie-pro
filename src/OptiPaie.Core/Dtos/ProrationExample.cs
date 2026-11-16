using System.Collections.Generic;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A live, side-by-side worked example of the proration deduction under each basis, built from
    /// a real employee so the user can SEE what the setting does before choosing it.
    /// </summary>
    public sealed class ProrationExample
    {
        public string EmployeeName { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal AbsentDays { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>The month's real day count (the MonthDays divisor).</summary>
        public int MonthDays { get; set; }

        /// <summary>The workable days from the calendar (the WorkingDays divisor).</summary>
        public int WorkableDays { get; set; }

        public IReadOnlyList<ProrationExampleRow> Rows { get; set; } = new List<ProrationExampleRow>();
    }

    /// <summary>One basis in the worked example: its divisor and the resulting deduction.</summary>
    public sealed class ProrationExampleRow
    {
        public ProrationBasis Basis { get; set; }
        public decimal Denominator { get; set; }
        public decimal Deduction { get; set; }
        public bool IsCurrentDefault { get; set; }
    }
}
