using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// Thin, read-only gate over the current license snapshot. The UI uses this to
    /// decide whether to open a module or show it locked (🔒). Kept separate from
    /// <see cref="ILicensingService"/> so views depend only on the question they ask.
    /// <para>
    /// While the free 48-hour trial is active, EVERY module is unlocked, so the demo
    /// shows the whole product (all HR modules), not just Payroll. A real license then
    /// unlocks exactly the modules it grants.
    /// </para>
    /// </summary>
    public sealed class LicenseGate : ILicenseGate
    {
        private readonly ILicensingService _licensing;
        private readonly ITrialService _trial;

        public LicenseGate(ILicensingService licensing, ITrialService trial)
        {
            _licensing = Guard.AgainstNull(licensing, nameof(licensing));
            _trial = Guard.AgainstNull(trial, nameof(trial));
        }

        public bool IsActivated => _licensing.Current.IsActivated;

        /// <summary>Usable when a license is active OR the trial is still running.</summary>
        public bool IsUsable => _licensing.Current.IsUsable || _trial.GetStatus().IsActive;

        public bool IsEnabled(string moduleKey)
        {
            // During the trial the whole product is unlocked; otherwise the license decides.
            if (_trial.GetStatus().IsActive)
            {
                return !string.IsNullOrEmpty(moduleKey);
            }

            return _licensing.Current.IsModuleEnabled(moduleKey);
        }
    }
}
