using System;
using System.Configuration;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// Builds the <see cref="LicensingOptions"/> for this build. The endpoint URLs and
    /// the public anon key come from the application config (so they can change without
    /// a rebuild), while the Ed25519 public key is embedded here for tamper resistance.
    /// <para>
    /// SETUP (after creating the Supabase project — see backend/README.md):
    ///   1. Run <c>deno run backend/scripts/generate-keypair.ts</c>.
    ///   2. Paste the PUBLIC key hex into <see cref="PublicKeyHex"/> below.
    ///   3. Put the function base URL and anon key in OptiPaie.Desktop's App.config.
    /// Until then <see cref="LicensingOptions.IsConfigured"/> is false and the app runs
    /// offline-only (no activation), without crashing.
    /// </para>
    /// </summary>
    public static class LicensingConfig
    {
        /// <summary>This build licenses the Payroll product.</summary>
        private const string ProductKey = ModuleKeys.Payroll;

        /// <summary>
        /// Ed25519 PUBLIC key (hex) that verifies signed license tokens offline.
        /// Its private counterpart is stored as the Supabase secret
        /// LICENSE_SIGNING_PRIVATE_KEY. Embedded (not in config) so a user cannot swap
        /// in their own key. Verified end-to-end against a token signed by the matching
        /// private key.
        /// </summary>
        private const string PublicKeyHex = "7b1ecdbbec68001d4e61496247f75512bba9ef606266af7ec842d0ac3c17cb19";

        public static LicensingOptions Build()
        {
            string baseUrl = AppSetting("Licensing.BaseUrl");
            string activateUrl = AppSetting("Licensing.ActivateUrl");
            string validateUrl = AppSetting("Licensing.ValidateUrl");
            string anonKey = AppSetting("Licensing.AnonKey");

            if (string.IsNullOrWhiteSpace(activateUrl) && !string.IsNullOrWhiteSpace(baseUrl))
            {
                activateUrl = baseUrl.TrimEnd('/') + "/activate";
            }

            if (string.IsNullOrWhiteSpace(validateUrl) && !string.IsNullOrWhiteSpace(baseUrl))
            {
                validateUrl = baseUrl.TrimEnd('/') + "/validate";
            }

            return new LicensingOptions
            {
                ProductKey = ProductKey,
                ActivateUrl = activateUrl ?? string.Empty,
                ValidateUrl = validateUrl ?? string.Empty,
                AnonKey = anonKey ?? string.Empty,
                PublicKeyHex = PublicKeyHex,
                AppVersion = ResolveAppVersion()
            };
        }

        private static string AppSetting(string key)
        {
            try
            {
                return ConfigurationManager.AppSettings[key];
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveAppVersion()
        {
            // The PRODUCT version (InformationalVersion, e.g. "1.41.0") — the SAME field the updater
            // compares — NOT GetName().Version. Reading the assembly version here reported every
            // device's app version to the licensing backend as the frozen placeholder.
            return OptiPaie.Core.Updates.AppVersion.CurrentProduct();
        }
    }
}
