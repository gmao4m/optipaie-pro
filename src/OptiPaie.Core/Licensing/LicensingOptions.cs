namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Configuration for the licensing layer. The endpoint URLs and the public
    /// anon key come from configuration (so they can change without a code change);
    /// the Ed25519 public key is embedded by the host for tamper resistance. The
    /// product key identifies which product this build licenses.
    /// </summary>
    public sealed class LicensingOptions
    {
        /// <summary>This build's product key (e.g. "payroll").</summary>
        public string ProductKey { get; set; }

        /// <summary>Full URL of the activation endpoint.</summary>
        public string ActivateUrl { get; set; }

        /// <summary>Full URL of the validation endpoint.</summary>
        public string ValidateUrl { get; set; }

        /// <summary>Public anon key sent as the API authorisation (safe to embed).</summary>
        public string AnonKey { get; set; }

        /// <summary>Ed25519 public key (hex) used to verify signed tokens offline.</summary>
        public string PublicKeyHex { get; set; }

        /// <summary>This application's version, reported to the backend.</summary>
        public string AppVersion { get; set; }

        /// <summary>
        /// True when every value needed to reach the backend is present. Lets the app
        /// fail gracefully (and stay offline-only) if the cloud has not been wired yet.
        /// </summary>
        public bool IsConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(ActivateUrl)
                    && !string.IsNullOrWhiteSpace(ValidateUrl)
                    && !string.IsNullOrWhiteSpace(AnonKey)
                    && !string.IsNullOrWhiteSpace(PublicKeyHex)
                    && PublicKeyHex.IndexOf("REPLACE", System.StringComparison.OrdinalIgnoreCase) < 0;
            }
        }
    }
}
