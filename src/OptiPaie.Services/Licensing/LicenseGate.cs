using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// Thin, read-only gate over the current license snapshot. Kept separate from
    /// <see cref="ILicensingService"/> so views depend only on the question they ask.
    /// <para>
    /// Every license — and the free 48-hour trial — unlocks the WHOLE product: all
    /// sections (payroll, attendance, leave, loans, performance, contracts, training,
    /// assets, certificates, ATS/DRT, declarations, reports, archive…) are always on.
    /// There is no per-module licensing; the only axes a license varies on are company
    /// scope (Mono/Multi) and duration (lifetime/annual).
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
            // Every usable license (and the trial) unlocks the entire product — there is no
            // per-module gating any more. A section is available whenever the app is usable.
            return IsUsable && !string.IsNullOrEmpty(moduleKey);
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
