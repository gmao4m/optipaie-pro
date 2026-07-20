using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Local (offline) license management. Online validation is intentionally out of
    /// scope for now; the architecture is in place to add it later.
    /// </summary>
    public interface ILicenseService
    {
        /// <summary>Returns the current license state.</summary>
        LicenseInfo GetStatus();

        /// <summary>The stable identifier of this machine.</summary>
        string GetMachineId();

        /// <summary>Activates a license with the given serial and customer name.</summary>
        Result Activate(string serialNumber, string customerName);
    }
}
