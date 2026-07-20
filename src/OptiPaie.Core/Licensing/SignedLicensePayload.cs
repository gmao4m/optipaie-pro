using System;

namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// The payload carried inside a signed license token, as issued by the backend.
    /// Deserialised by the verifier AFTER the Ed25519 signature has been checked, so
    /// these values can be trusted. Property names map (case-insensitively) to the
    /// camelCase JSON produced by the Edge Functions.
    /// </summary>
    public sealed class SignedLicensePayload
    {
        /// <summary>Payload schema version.</summary>
        public int V { get; set; }

        /// <summary>Product key the license belongs to (must match this app's product).</summary>
        public string Product { get; set; }

        /// <summary>Latest released version of the product (informational).</summary>
        public string ProductVersion { get; set; }

        public string LicenseKey { get; set; }
        public string CompanyName { get; set; }
        public string CompanyId { get; set; }
        public string Email { get; set; }

        /// <summary>The device this license is bound to (must match this machine).</summary>
        public string DeviceId { get; set; }

        /// <summary>Server status: active | suspended | revoked.</summary>
        public string Status { get; set; }

        /// <summary>License type: trial | lifetime | annual | monthly | demo | enterprise.</summary>
        public string Type { get; set; }

        /// <summary>Customer display name (for the activation confirmation and Settings).</summary>
        public string CustomerName { get; set; }

        /// <summary>The enabled module keys.</summary>
        public string[] Modules { get; set; }

        public DateTime? IssuedAt { get; set; }

        /// <summary>Null = perpetual license.</summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>End of the offline grace window (issuedAt + product grace days).</summary>
        public DateTime? GraceUntil { get; set; }
    }
}
