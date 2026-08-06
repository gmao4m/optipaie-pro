using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace OptiPaie.Admin.Api
{
    /// <summary>
    /// Lightweight auto-update check for the admin console. Reads a hosted
    /// <c>admin-version.json</c> and, when a newer version exists, offers to open the
    /// download page. No silent install (the owner runs a single machine) — just a
    /// clear one-click prompt. Best-effort: any failure is swallowed and never blocks.
    /// </summary>
    public static class AdminUpdater
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        /// <summary>The running console version, read from the informational version (= &lt;Version&gt;).</summary>
        public static string CurrentVersion
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string v = attr != null ? attr.InformationalVersion : null;
                if (!string.IsNullOrWhiteSpace(v))
                {
                    int plus = v.IndexOf('+'); // strip "+<git-sha>" build metadata
                    if (plus > 0) v = v.Substring(0, plus);
                    return v.Trim();
                }
                Version av = Assembly.GetExecutingAssembly().GetName().Version;
                return av != null ? av.ToString(3) : "1.0.0";
            }
        }

        public static async Task CheckAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                string body = await Http.GetStringAsync(url).ConfigureAwait(true);
                JObject o = JObject.Parse(body);
                string latest = (string)o["latest_version"];
                string download = (string)o["download_url"];
                if (string.IsNullOrWhiteSpace(latest) || !IsNewer(latest, CurrentVersion)) return;

                MessageBoxResult r = MessageBox.Show(
                    "Une nouvelle version de la console Admin est disponible : " + latest +
                    "  (version actuelle : " + CurrentVersion + ").\r\n\r\nVoulez-vous la télécharger maintenant ?",
                    "Mise à jour disponible", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (r == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(download))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(download) { UseShellExecute = true });
                }
            }
            catch { /* update check is best-effort; never interrupts the console */ }
        }

        private static bool IsNewer(string latest, string current)
        {
            return TryVer(latest, out Version l) && TryVer(current, out Version c) && l > c;
        }

        private static bool TryVer(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return Version.TryParse(s.Trim().TrimStart('v', 'V'), out v);
        }
    }
}
