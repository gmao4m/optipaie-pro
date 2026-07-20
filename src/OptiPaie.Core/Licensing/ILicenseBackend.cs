using System.Threading;
using System.Threading.Tasks;

namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Provider-agnostic transport to the licensing cloud. The ONLY seam that knows
    /// how to talk to a specific backend (Supabase today). Swapping providers later
    /// means implementing this interface once — nothing else in the app changes.
    /// <para>
    /// Implementations MUST throw on transport failure (no internet, timeout); a
    /// non-2xx HTTP response is returned as an unsuccessful <see cref="BackendLicenseResponse"/>.
    /// </para>
    /// </summary>
    public interface ILicenseBackend
    {
        Task<BackendLicenseResponse> ActivateAsync(ActivationRequest request, CancellationToken cancellationToken);

        Task<BackendLicenseResponse> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Redeems a single-use module activation key. On success returns a fresh signed
        /// token that now includes the unlocked module. The server enforces single-use.
        /// </summary>
        Task<BackendLicenseResponse> ActivateModuleAsync(ModuleActivationRequest request, CancellationToken cancellationToken);
    }
}
