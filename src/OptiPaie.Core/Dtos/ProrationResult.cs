using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// The resolved proration for one employee/month under the company's basis: the worked and
    /// workable days the engine receives, the basis and divisor used (for the review table), and the
    /// days that reduce pay. For the default basis (<see cref="ProrationBasis.MonthDays"/>) this
    /// reproduces the product's historical computation exactly.
    /// </summary>
    public sealed class ProrationResult
    {
        public decimal WorkedDays { get; set; }
        public decimal WorkableDays { get; set; }
        public ProrationBasis Basis { get; set; }

        /// <summary>The divisor the deduction uses (= WorkableDays): month days, 30, or workable days.</summary>
        public decimal Divisor { get; set; }

        /// <summary>Days that reduce pay = unjustified absence + half of the half-days. Nothing else.</summary>
        public decimal DeductibleDays { get; set; }

        /// <summary>False when the employee has no attendance record for the month (then full month, said plainly).</summary>
        public bool HasAttendance { get; set; }
    }
}
