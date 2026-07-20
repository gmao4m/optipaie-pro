using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>Snapshot of the current license state, shown by the License Manager.</summary>
    public sealed class LicenseInfo
    {
        public LicenseStatus Status { get; set; }
        public string CustomerName { get; set; }
        public string SerialNumber { get; set; }
        public string MachineId { get; set; }
        public DateTime? ExpirationUtc { get; set; }
    }
}
