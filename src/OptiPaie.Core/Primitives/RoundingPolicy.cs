using System;

namespace OptiPaie.Core.Primitives
{
    /// <summary>
    /// The single authority for monetary rounding across the payroll engine.
    /// <para>
    /// Per the approved Payroll Engine Specification (§10), intermediate
    /// calculations keep full <see cref="decimal"/> precision and rounding happens
    /// only at the defined statutory points (CNAS, IRG, Net). The specification
    /// flags the rounding granularity (whole dinar vs. centime) as a single switch
    /// to be confirmed against a real payslip — that switch is exactly this object:
    /// inject <see cref="Centime"/> or <see cref="WholeDinar"/> and the whole engine
    /// follows it. This keeps the rule in one place (DRY) and never uses binary
    /// floating point.
    /// </para>
    /// </summary>
    public sealed class RoundingPolicy
    {
        /// <summary>Number of decimal places kept after rounding.</summary>
        public int Scale { get; }

        /// <summary>Midpoint handling. Algerian manual payroll rounds halves away from zero.</summary>
        public MidpointRounding Mode { get; }

        /// <summary>Creates a rounding policy with the given scale and midpoint mode.</summary>
        /// <param name="scale">Decimal places (0 = whole dinar, 2 = centime). Must be 0..4.</param>
        /// <param name="mode">Midpoint rounding mode.</param>
        public RoundingPolicy(int scale, MidpointRounding mode)
        {
            if (scale < 0 || scale > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), scale,
                    "Rounding scale must be between 0 and 4 decimal places.");
            }

            Scale = scale;
            Mode = mode;
        }

        /// <summary>Centime precision: 2 decimal places, halves away from zero.</summary>
        public static RoundingPolicy Centime { get; } =
            new RoundingPolicy(2, MidpointRounding.AwayFromZero);

        /// <summary>Whole-dinar precision: 0 decimal places, halves away from zero.</summary>
        public static RoundingPolicy WholeDinar { get; } =
            new RoundingPolicy(0, MidpointRounding.AwayFromZero);

        /// <summary>Rounds the given amount according to this policy.</summary>
        public decimal Round(decimal amount)
        {
            return Math.Round(amount, Scale, Mode);
        }
    }
}
