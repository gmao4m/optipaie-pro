using System;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// An immutable snapshot of the legal/configurable values the payroll engine
    /// needs for one calculation: the CNAS rates, the SNMG and the rounding policy.
    /// <para>
    /// The engine receives this as input and never reads configuration or the
    /// database itself — keeping it pure. The IRG barème, abattement and lissage
    /// are not here: they are fixed in the engine per the approved decisions.
    /// </para>
    /// </summary>
    public sealed class LegalSnapshot
    {
        /// <summary>CNAS employee contribution rate as a fraction (e.g. 0.09 for 9%).</summary>
        public decimal CnasEmployeeRate { get; }

        /// <summary>CNAS employer contribution rate as a fraction (e.g. 0.26 for 26%).</summary>
        public decimal CnasEmployerRate { get; }

        /// <summary>Salaire National Minimum Garanti, in dinars.</summary>
        public decimal Snmg { get; }

        /// <summary>The rounding policy applied at the statutory points (CNAS, IRG, Net).</summary>
        public RoundingPolicy Rounding { get; }

        /// <summary>Creates a legal snapshot.</summary>
        public LegalSnapshot(decimal cnasEmployeeRate, decimal cnasEmployerRate, decimal snmg, RoundingPolicy rounding)
        {
            if (cnasEmployeeRate < 0m || cnasEmployeeRate > 1m)
            {
                throw new ArgumentOutOfRangeException(nameof(cnasEmployeeRate), cnasEmployeeRate,
                    "CNAS employee rate must be a fraction between 0 and 1.");
            }

            if (cnasEmployerRate < 0m || cnasEmployerRate > 1m)
            {
                throw new ArgumentOutOfRangeException(nameof(cnasEmployerRate), cnasEmployerRate,
                    "CNAS employer rate must be a fraction between 0 and 1.");
            }

            if (snmg < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(snmg), snmg, "SNMG cannot be negative.");
            }

            CnasEmployeeRate = cnasEmployeeRate;
            CnasEmployerRate = cnasEmployerRate;
            Snmg = snmg;
            Rounding = rounding ?? throw new ArgumentNullException(nameof(rounding));
        }
    }
}
