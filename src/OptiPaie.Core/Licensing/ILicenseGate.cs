namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// The single question the UI asks to decide whether a module is available:
    /// a thin, read-only view over the current license snapshot. Navigation renders
    /// every module from the registry and uses this to show it unlocked or 🔒.
    /// </summary>
    public interface ILicenseGate
    {
        /// <summary>True when a license is activated on this machine.</summary>
        bool IsActivated { get; }

        /// <summary>True when the license is currently valid and usable.</summary>
        bool IsUsable { get; }

        /// <summary>True when the given module is currently unlocked.</summary>
        bool IsEnabled(string moduleKey);

        /// <summary>
        /// The number of companies this installation may create.
        /// <c>1</c> = Mono-société; <c>null</c> = Multi-sociétés (unlimited).
        /// A trial or an unactivated app is treated as Mono (1).
        /// </summary>
        int? MaxCompanies { get; }

        /// <summary>
        /// Whether another company may be created given how many already exist.
        /// The first company is always allowed (so onboarding/trial works); further
        /// ones require a Multi-sociétés license.
        /// </summary>
        bool CanAddCompany(int currentCompanyCount);
    }
}
