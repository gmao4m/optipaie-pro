using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// Thin, read-only gate over the current license snapshot. The UI uses this to
    /// decide whether to open a module or show it locked (🔒). Kept separate from
    /// <see cref="ILicensingService"/> so views depend only on the question they ask.
    /// </summary>
    public sealed class LicenseGate : ILicenseGate
    {
        private readonly ILicensingService _licensing;

        public LicenseGate(ILicensingService licensing)
        {
            _licensing = Guard.AgainstNull(licensing, nameof(licensing));
        }

        public bool IsActivated => _licensing.Current.IsActivated;

        public bool IsUsable => _licensing.Current.IsUsable;

        public bool IsEnabled(string moduleKey)
        {
            return _licensing.Current.IsModuleEnabled(moduleKey);
        }
    }
}
