using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// One row of the pre-run review the user confirms before any payslip is issued: what the
    /// attendance means for this employee's pay this month, and the basis/divisor used. "Only
    /// unjustified absence (and half of a half-day) reduces pay" is exactly what <see cref="Deduction"/>
    /// reflects. A company with no attendance for the employee shows <see cref="NoAttendance"/> plainly.
    /// </summary>
    public sealed class PayrollReviewRow
    {
        public long EmployeeId { get; set; }
        public string Employee { get; set; }

        /// <summary>Days due = the divisor (month days, 30, or the calendar's workable days).</summary>
        public decimal DaysDue { get; set; }

        public decimal Worked { get; set; }

        /// <summary>Unjustified absence days (reduce pay).</summary>
        public decimal Absent { get; set; }

        /// <summary>Approved leave + justified absence (do NOT reduce pay).</summary>
        public decimal Leave { get; set; }

        /// <summary>Half days (each reduces pay by 0.5).</summary>
        public decimal Half { get; set; }

        /// <summary>The pay reduction = base × (unjustified absence + half of the half-days) ÷ divisor.</summary>
        public decimal Deduction { get; set; }

        public ProrationBasis Basis { get; set; }
        public decimal Divisor { get; set; }

        /// <summary>True when the employee has NO attendance record this month (full month, said plainly, not silently).</summary>
        public bool NoAttendance { get; set; }
    }
}
