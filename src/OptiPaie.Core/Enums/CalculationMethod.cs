namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// How a payroll element's amount is computed. V1 supports these four generic
    /// methods, which realise every approved element archetype:
    /// Manual Value &amp; fixed primes → <see cref="FixedAmount"/>;
    /// Overtime (hours × rate) → <see cref="QuantityUnitPrice"/>;
    /// Seniority/IEP (base × rate × years) → <see cref="BaseRate"/>;
    /// percentage-of-base primes → <see cref="Percentage"/>.
    /// <para>
    /// A free-form Formula method is intentionally NOT implemented in V1. The
    /// strategy-based design lets a Formula method be added in V2 (new enum value +
    /// new strategy) without touching the existing engine.
    /// </para>
    /// </summary>
    public enum CalculationMethod
    {
        /// <summary>A fixed amount entered directly (also covers "manual value").</summary>
        FixedAmount = 1,

        /// <summary>A percentage applied to a referenced base (see <see cref="CalculationBase"/>).</summary>
        Percentage = 2,

        /// <summary>Quantity multiplied by a unit price (e.g. overtime hours × hourly rate).</summary>
        QuantityUnitPrice = 3,

        /// <summary>A referenced base multiplied by a rate (e.g. ancienneté/IEP = base × rate × years).</summary>
        BaseRate = 4
    }
}
