namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// The periodicity of a payroll element, which drives IRG lissage
    /// (smoothing) for non-monthly amounts per the specification (§6).
    /// </summary>
    public enum ElementPeriodicity
    {
        /// <summary>Paid every month; taxed directly in the month (no lissage).</summary>
        Monthly = 1,

        /// <summary>Paid every three months; lissed over its reference months.</summary>
        Quarterly = 2,

        /// <summary>Paid once a year; lissed over its reference months.</summary>
        Annual = 3,

        /// <summary>One-off / exceptional (e.g. rappel, prime exceptionnelle); lissed when tied to a period.</summary>
        OneOff = 4
    }
}
