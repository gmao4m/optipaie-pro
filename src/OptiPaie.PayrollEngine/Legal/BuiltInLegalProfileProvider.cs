using System;
using System.Collections.Generic;
using OptiPaie.Core.Primitives;

namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>
    /// Supplies the built-in, versioned legal profiles. Currently the 2026 profile
    /// (CIDTA Art. 104, LF 2022, in force 2022–2026). The most recent profile whose
    /// effective date is on or before the period is selected, so adding a future
    /// Finance Law is just adding another profile to this list.
    /// </summary>
    public sealed class BuiltInLegalProfileProvider : ILegalProfileProvider
    {
        private readonly List<LegalProfile> _profiles;

        public BuiltInLegalProfileProvider()
        {
            _profiles = new List<LegalProfile> { CreateDz2026() };
        }

        public LegalProfile GetProfile(PayrollPeriod period)
        {
            DateTime periodStart = period.FirstDay;
            LegalProfile selected = null;

            foreach (LegalProfile profile in _profiles)
            {
                if (profile.EffectiveFrom <= periodStart &&
                    (selected == null || profile.EffectiveFrom > selected.EffectiveFrom))
                {
                    selected = profile;
                }
            }

            // If the period predates the earliest profile, fall back to the earliest.
            if (selected == null && _profiles.Count > 0)
            {
                selected = _profiles[0];
            }

            return selected;
        }

        /// <summary>
        /// The 2026 Algerian legal profile. Monthly IRG barème (marginal), exemption
        /// at 30 000, abattement 40% clamped 1 000–1 500, smoothing on 30 000–35 000
        /// with factor 137/51 and subtrahend 27925/8.
        /// </summary>
        private static LegalProfile CreateDz2026()
        {
            var brackets = new List<IrgBracket>
            {
                new IrgBracket(0m, 20000m, 0.00m),
                new IrgBracket(20000m, 40000m, 0.23m),
                new IrgBracket(40000m, 80000m, 0.27m),
                new IrgBracket(80000m, 160000m, 0.30m),
                new IrgBracket(160000m, 320000m, 0.33m),
                new IrgBracket(320000m, null, 0.35m)
            };

            var abattement = new AbattementRule(0.40m, 1000m, 1500m);

            var smoothing = new SmoothingRule(
                lowerBound: 30000m,
                upperBound: 35000m,
                multiplierNumerator: 137m,
                multiplierDenominator: 51m,
                subtrahendNumerator: 27925m,
                subtrahendDenominator: 8m);

            return new LegalProfile(
                legalVersion: "DZ-2026",
                effectiveFrom: new DateTime(2022, 1, 1),
                irgBrackets: brackets,
                exemptionThreshold: 30000m,
                abattement: abattement,
                smoothing: smoothing,
                lissageMethod: LissageMethod.Differential);
        }
    }
}
