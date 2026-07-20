namespace OptiPaie.Core.Licensing
{
    /// <summary>Request to activate a license on this device.</summary>
    public sealed class ActivationRequest
    {
        public string ProductKey { get; set; }
        public string LicenseKey { get; set; }
        public string DeviceId { get; set; }
        public string CompanyName { get; set; }
        public string Email { get; set; }
        public string AppVersion { get; set; }
    }

    /// <summary>Request to (re)validate an existing license and sync module permissions.</summary>
    public sealed class ValidationRequest
    {
        public string ProductKey { get; set; }
        public string LicenseKey { get; set; }
        public string DeviceId { get; set; }
        public string AppVersion { get; set; }
    }

    /// <summary>Request to redeem a single-use module activation key.</summary>
    public sealed class ModuleActivationRequest
    {
        public string ProductKey { get; set; }
        public string LicenseKey { get; set; }
        public string DeviceId { get; set; }
        public string ActivationKey { get; set; }
        public string AppVersion { get; set; }
    }

    /// <summary>
    /// The raw result of a backend call, provider-agnostic. <see cref="Success"/> is
    /// true for an HTTP 2xx (a signed <see cref="Token"/> is present); otherwise
    /// <see cref="ErrorCode"/> carries the backend error (e.g. "invalid_key").
    /// Transport failures (no internet) are surfaced as exceptions, not this object.
    /// </summary>
    public sealed class BackendLicenseResponse
    {
        public bool Success { get; set; }
        public string Token { get; set; }
        public string Status { get; set; }
        public string[] Modules { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
    }
}
