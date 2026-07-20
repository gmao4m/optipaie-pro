using System;
using System.Threading;
using System.Threading.Tasks;

namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// The application-facing licensing orchestrator. Owns the current license
    /// snapshot, performs offline verification at startup, and talks to the backend
    /// only for activation and synchronization. All employee/payroll/company data
    /// stays local and is never sent anywhere.
    /// </summary>
    public interface ILicensingService
    {
        /// <summary>The current, cached license snapshot (never null).</summary>
        LicenseSnapshot Current { get; }

        /// <summary>This machine's stable device id.</summary>
        string DeviceId { get; }

        /// <summary>Raised whenever <see cref="Current"/> changes (e.g. after a sync).</summary>
        event EventHandler Changed;

        /// <summary>
        /// Reloads and re-verifies the local license offline (no network). Called at
        /// startup and whenever the app wants to re-read the cached state.
        /// </summary>
        LicenseSnapshot Refresh();

        /// <summary>Activates a license key on this device (online).</summary>
        Task<LicenseResult> ActivateAsync(
            string licenseKey, string companyName, string email, CancellationToken cancellationToken);

        /// <summary>
        /// Re-validates the current license and syncs module permissions (online).
        /// On no internet, returns <see cref="LicenseResultKind.Offline"/> and keeps
        /// the last valid local license.
        /// </summary>
        Task<LicenseResult> SynchronizeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Redeems a single-use module activation key: validates it online, unlocks the
        /// module, and updates the encrypted local cache. Requires an activated license
        /// and internet. Raises <see cref="Changed"/> so the UI unlocks immediately.
        /// </summary>
        Task<LicenseResult> ActivateModuleAsync(string activationKey, CancellationToken cancellationToken);

        /// <summary>
        /// Removes the local license cache from this device (deactivation). The next
        /// launch requires activation again. Does not delete any business data.
        /// </summary>
        void Deactivate();
    }
}
