using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// Combines the license state and the trial into a single access decision used at
    /// startup and by the Settings/License page. Priority: an active license wins;
    /// otherwise an active trial; otherwise a locked (suspended/expired) license;
    /// otherwise an expired trial; otherwise "not activated".
    /// </summary>
    public sealed class AccessGate : IAccessController
    {
        private readonly ILicensingService _licensing;
        private readonly ITrialService _trial;

        public AccessGate(ILicensingService licensing, ITrialService trial)
        {
            _licensing = Guard.AgainstNull(licensing, nameof(licensing));
            _trial = Guard.AgainstNull(trial, nameof(trial));
        }

        public AccessEvaluation Evaluate()
        {
            LicenseSnapshot license = _licensing.Current;
            TrialInfo trial = _trial.GetStatus();

            if (license.IsUsable)
            {
                return new AccessEvaluation(AccessState.Licensed, license, trial);
            }

            if (trial.IsActive)
            {
                return new AccessEvaluation(AccessState.Trial, license, trial);
            }

            if (license.IsActivated)
            {
                // A license is present but not usable (suspended / revoked / expired).
                return new AccessEvaluation(AccessState.Locked, license, trial);
            }

            if (trial.IsExpired)
            {
                return new AccessEvaluation(AccessState.TrialExpired, license, trial);
            }

            return new AccessEvaluation(AccessState.NotActivated, license, trial);
        }
    }
}
