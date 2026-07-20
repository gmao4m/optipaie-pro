using System;
using System.Configuration;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Updates;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// Builds <see cref="UpdateOptions"/> from configuration. The Velopack feed URL
    /// comes from App.config (no hardcoded URL); the update-metadata endpoint and the
    /// publishable key are derived from the already-configured licensing project, so
    /// there is nothing extra to wire.
    /// </summary>
    public static class UpdateConfig
    {
        public static UpdateOptions Build(LicensingOptions licensing)
        {
            string feedUrl = AppSetting("Update.FeedUrl");

            // Derive the `updates` REST endpoint from the licensing base:
            //   https://<ref>.supabase.co/functions/v1/activate → .../rest/v1/updates
            string metadataUrl = string.Empty;
            string activate = licensing != null ? licensing.ActivateUrl : null;
            if (!string.IsNullOrWhiteSpace(activate))
            {
                int idx = activate.IndexOf("/functions/v1", StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    metadataUrl = activate.Substring(0, idx) + "/rest/v1/updates";
                }
            }

            return new UpdateOptions
            {
                AppName = "OptiPaie PRO",
                GitHubRepo = AppSetting("Update.GitHubRepo") ?? string.Empty,
                FeedUrl = feedUrl ?? string.Empty,
                MetadataUrl = metadataUrl,
                AnonKey = licensing != null ? licensing.AnonKey : string.Empty
            };
        }

        private static string AppSetting(string key)
        {
            try { return ConfigurationManager.AppSettings[key]; }
            catch { return null; }
        }
    }
}
