namespace OptiPaie.Core.Licensing
{
    /// <summary>Outcome category of an activation or synchronization attempt.</summary>
    public enum LicenseResultKind
    {
        /// <summary>Completed successfully; the resulting state is in the snapshot.</summary>
        Ok = 0,

        /// <summary>No internet — the last valid local license continues to be used.</summary>
        Offline = 1,

        /// <summary>Nothing to synchronize because no license is activated.</summary>
        NotActivated = 2,

        /// <summary>The licensing cloud is not configured in this build yet.</summary>
        NotConfigured = 3,

        /// <summary>The license key does not exist.</summary>
        InvalidKey = 4,

        /// <summary>The key is already activated on another device.</summary>
        DeviceInUse = 5,

        /// <summary>The key is bound to a different device than this one.</summary>
        DeviceMismatch = 6,

        /// <summary>The license was suspended.</summary>
        Suspended = 7,

        /// <summary>The license was revoked.</summary>
        Revoked = 8,

        /// <summary>The key belongs to a different product.</summary>
        WrongProduct = 9,

        /// <summary>The server did not recognise this product.</summary>
        UnknownProduct = 10,

        /// <summary>The module activation key does not exist / is malformed.</summary>
        KeyInvalid = 12,

        /// <summary>The module activation key has already been used.</summary>
        KeyUsed = 13,

        /// <summary>The module activation key was revoked.</summary>
        KeyRevoked = 14,

        /// <summary>The module activation key has expired.</summary>
        KeyExpired = 15,

        /// <summary>The key belongs to a different license.</summary>
        KeyWrongLicense = 16,

        /// <summary>The module is already active on this license.</summary>
        ModuleAlreadyActive = 17,

        /// <summary>An unexpected error occurred.</summary>
        Error = 11
    }

    /// <summary>The result of an activation or synchronization call.</summary>
    public sealed class LicenseResult
    {
        private LicenseResult(LicenseResultKind kind, string message, LicenseSnapshot snapshot)
        {
            Kind = kind;
            Message = message ?? string.Empty;
            Snapshot = snapshot;
        }

        public LicenseResultKind Kind { get; }

        /// <summary>A ready-to-show French message (empty on success).</summary>
        public string Message { get; }

        /// <summary>The license state after the call (may be null for hard errors).</summary>
        public LicenseSnapshot Snapshot { get; }

        public bool IsSuccess => Kind == LicenseResultKind.Ok;

        public static LicenseResult Ok(LicenseSnapshot snapshot)
        {
            return new LicenseResult(LicenseResultKind.Ok, string.Empty, snapshot);
        }

        public static LicenseResult Create(LicenseResultKind kind, string message, LicenseSnapshot snapshot)
        {
            return new LicenseResult(kind, message, snapshot);
        }
    }
}
