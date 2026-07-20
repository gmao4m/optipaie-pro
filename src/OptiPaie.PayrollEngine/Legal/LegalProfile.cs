using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>
    /// An immutable, versioned set of the legal payroll rules that are fixed in the
    /// engine: the IRG barème, the exemption threshold, the abattement, the
    /// low-income smoothing and the lissage method. A future Finance Law is a new
    /// profile version — the calculation code never changes, and it never contains a
    /// legal literal (every value is read from this profile).
    /// <para>
    /// CNAS rates and the SNMG are NOT here: they come from the configurable
    /// LegalParameters table via the legal snapshot.
    /// </para>
    /// </summary>
    public sealed class LegalProfile
    {
        /// <summary>Legal version identifier (e.g. "DZ-2026").</summary>
        public string LegalVersion { get; }

        /// <summary>Date from which this profile is effective.</summary>
        public DateTime EffectiveFrom { get; }

        /// <summary>The monthly IRG barème, ordered ascending by lower bound.</summary>
        public IReadOnlyList<IrgBracket> IrgBrackets { get; }

        /// <summary>Taxable base at or below which IRG is fully exempt.</summary>
        public decimal ExemptionThreshold { get; }

        /// <summary>The abattement parameters.</summary>
        public AbattementRule Abattement { get; }

        /// <summary>The low-income smoothing rule.</summary>
        public SmoothingRule Smoothing { get; }

        /// <summary>The lissage method used for non-monthly amounts.</summary>
        public LissageMethod LissageMethod { get; }

        public LegalProfile(
            string legalVersion,
            DateTime effectiveFrom,
            IEnumerable<IrgBracket> irgBrackets,
            decimal exemptionThreshold,
            AbattementRule abattement,
            SmoothingRule smoothing,
            LissageMethod lissageMethod)
        {
            if (string.IsNullOrWhiteSpace(legalVersion))
            {
                throw new ArgumentException("Legal version is required.", nameof(legalVersion));
            }

            LegalVersion = legalVersion;
            EffectiveFrom = effectiveFrom;
            IrgBrackets = new ReadOnlyCollection<IrgBracket>(new List<IrgBracket>(
                irgBrackets ?? throw new ArgumentNullException(nameof(irgBrackets))));
            ExemptionThreshold = exemptionThreshold;
            Abattement = abattement ?? throw new ArgumentNullException(nameof(abattement));
            Smoothing = smoothing ?? throw new ArgumentNullException(nameof(smoothing));
            LissageMethod = lissageMethod;
        }
    }
}
