using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Immutable input describing how a non-monthly amount (rappel or periodic
    /// prime) is spread for IRG, per the specification (§6). The service supplies
    /// the taxable base of each concerned month; the engine applies the
    /// differential method over those months.
    /// </summary>
    public sealed class LissageInput
    {
        /// <summary>Number of reference months over which the amount is spread.</summary>
        public int Months { get; }

        /// <summary>
        /// The existing Base Imposable of each concerned month (one entry per month),
        /// used as the baseline for the differential IRG calculation.
        /// </summary>
        public IReadOnlyList<decimal> ReferenceBases { get; }

        /// <summary>Creates a lissage input.</summary>
        public LissageInput(int months, IEnumerable<decimal> referenceBases)
        {
            if (months < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(months), months,
                    "Lissage must span at least one month.");
            }

            if (referenceBases == null)
            {
                throw new ArgumentNullException(nameof(referenceBases));
            }

            Months = months;
            ReferenceBases = new ReadOnlyCollection<decimal>(new List<decimal>(referenceBases));
        }
    }
}
