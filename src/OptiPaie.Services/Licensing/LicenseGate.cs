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

        /// <summary>
        /// The effective company cap (null = unlimited/Multi). A usable license decides
        /// its own scope: an explicit <c>0</c> means Multi (unlimited); any other value —
        /// including a missing/null token field — is read as Mono (the safe default that
        /// matches the backend). A running trial or an unactivated app is also Mono. So
        /// opening multi-company always requires a real, usable Multi-sociétés licence.
        /// </summary>
        public int? MaxCompanies
        {
            get
            {
                LicenseSnapshot snapshot = _licensing.Current;
                if (!snapshot.IsUsable)
                {
                    return 1; // trial / unactivated / suspended → Mono
                }

                int? raw = snapshot.MaxCompanies;
                if (raw.HasValue && raw.Value == 0)
                {
                    return null; // explicit 0 = Multi-sociétés (unlimited)
                }

                return raw ?? 1; // null/absent → Mono (safe); a positive N → N companies
            }
        }

        public bool CanAddCompany(int currentCompanyCount)
        {
            int? max = MaxCompanies;
            // null = unlimited (Multi). Otherwise allow while under the cap — so the
            // first company is always creatable even on a Mono/trial installation.
            return !max.HasValue || currentCompanyCount < max.Value;
        }
    }
}
