using System;
using OptiPaie.PayrollEngine.Money;

namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>
    /// Computes the monthly IRG for a taxable base, strictly from the values in the
    /// <see cref="LegalProfile"/> (no legal literals here). Implements the marginal
    /// barème, the abattement (clamped), the exemption and the low-income smoothing.
    /// This is the single source of the IRG math, reused by the IRG rule and the
    /// lissage rule (DRY), and independently unit-testable.
    /// </summary>
    public sealed class IrgCalculator
    {
        private readonly MoneyEngine _money;

        public IrgCalculator(MoneyEngine money)
        {
            _money = money ?? throw new ArgumentNullException(nameof(money));
        }

        /// <summary>Computes the full IRG (brut, abattement, final) for a taxable base.</summary>
        public IrgComputation Compute(decimal taxableBase, LegalProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            // Exemption: at or below the threshold there is no tax.
            if (taxableBase <= profile.ExemptionThreshold)
            {
                return new IrgComputation(0m, 0m, 0m);
            }

            decimal grossPrecise = ComputeBaremePrecise(taxableBase, profile);
            decimal abattementPrecise = ComputeAbattementPrecise(grossPrecise, profile.Abattement);
            decimal afterAbattement = grossPrecise - abattementPrecise;

            decimal finalPrecise;
            if (taxableBase > profile.Smoothing.LowerBound && taxableBase <= profile.Smoothing.UpperBound)
            {
                // Low-income smoothing band.
                finalPrecise =
                    (afterAbattement * profile.Smoothing.MultiplierNumerator / profile.Smoothing.MultiplierDenominator)
                    - (profile.Smoothing.SubtrahendNumerator / profile.Smoothing.SubtrahendDenominator);

                if (finalPrecise < 0m)
                {
                    finalPrecise = 0m;
                }
            }
            else
            {
                finalPrecise = afterAbattement;
            }

            return new IrgComputation(
                _money.Round(grossPrecise),
                _money.Round(abattementPrecise),
                _money.Round(finalPrecise));
        }

        /// <summary>Computes the gross IRG from the marginal barème (full precision).</summary>
        public decimal ComputeBaremePrecise(decimal taxableBase, LegalProfile profile)
        {
            if (taxableBase <= 0m)
            {
                return 0m;
            }

            decimal tax = 0m;

            foreach (IrgBracket bracket in profile.IrgBrackets)
            {
                if (taxableBase <= bracket.LowerBound)
                {
                    continue;
                }

                decimal upper = bracket.UpperBound ?? taxableBase;
                decimal ceiling = Math.Min(taxableBase, upper);
                decimal portion = ceiling - bracket.LowerBound;

                if (portion > 0m)
                {
                    tax += portion * bracket.Rate;
                }
            }

            return tax;
        }

        private static decimal ComputeAbattementPrecise(decimal grossIrg, AbattementRule rule)
        {
            if (grossIrg <= 0m)
            {
                return 0m;
            }

            decimal abattement = grossIrg * rule.Rate;

            if (abattement < rule.Min)
            {
                abattement = rule.Min;
            }
            else if (abattement > rule.Max)
            {
                abattement = rule.Max;
            }

            // The abattement can never exceed the gross IRG.
            if (abattement > grossIrg)
            {
                abattement = grossIrg;
            }

            return abattement;
        }
    }
}
