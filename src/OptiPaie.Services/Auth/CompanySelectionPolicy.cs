using System;

namespace OptiPaie.Services.Auth
{
    /// <summary>
    /// What the startup company gate must do, decided purely from how many companies exist.
    /// The WPF <c>App</c> reads this to drive the gate; tests assert the mapping — no WPF
    /// dependency, so the "one company → direct, several → picker, none → create" rule is
    /// reasoned about in unit tests.
    /// </summary>
    public enum CompanyStartupAction
    {
        /// <summary>No company yet — show a blocking screen offering to CREATE the first one.</summary>
        CreateFirst,

        /// <summary>Exactly one company — ENTER it directly: no screen, no question, no click.</summary>
        EnterDirect,

        /// <summary>Several companies — show a blocking PICKER before any data screen loads.</summary>
        Choose
    }

    /// <summary>
    /// Single source of truth for the startup company decision. A multi-company install must
    /// never silently auto-open the first company (that would show one client's data by
    /// accident), and a single-company install must never nag with a pointless picker.
    /// </summary>
    public static class CompanySelectionPolicy
    {
        public static CompanyStartupAction Decide(int companyCount)
        {
            if (companyCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(companyCount), "Le nombre de sociétés ne peut pas être négatif.");
            }

            if (companyCount == 0)
            {
                return CompanyStartupAction.CreateFirst;
            }

            if (companyCount == 1)
            {
                return CompanyStartupAction.EnterDirect;
            }

            return CompanyStartupAction.Choose;
        }
    }
}
