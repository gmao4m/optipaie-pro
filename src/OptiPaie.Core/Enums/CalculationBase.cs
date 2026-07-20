namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// The reference base a percentage- or rate-driven element is applied to.
    /// </summary>
    public enum CalculationBase
    {
        /// <summary>The employee's base salary (salaire de base).</summary>
        SalaireDeBase = 1,

        /// <summary>The gross salary (salaire brut) — sum of all gains.</summary>
        SalaireBrut = 2,

        /// <summary>The contributory base (base cotisable).</summary>
        BaseCotisable = 3
    }
}
