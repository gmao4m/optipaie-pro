namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// How a partial month's base salary is prorated. Set per company on the work calendar.
    /// The default, <see cref="MonthDays"/>, reproduces the product's historical behaviour
    /// exactly (divide by the month's real number of days), so no existing customer's numbers
    /// move unless they deliberately choose another basis. Changing it affects FUTURE runs only.
    /// </summary>
    public enum ProrationBasis
    {
        /// <summary>base ÷ the month's real day count (28–31). THE DEFAULT — today's behaviour.</summary>
        MonthDays = 0,

        /// <summary>base ÷ 30, always (fixed thirtieths). Opt-in; some collective agreements use it.</summary>
        FixedThirtieths = 1,

        /// <summary>base ÷ the workable days from the work calendar. Opt-in; the precise basis.</summary>
        WorkingDays = 2
    }
}
