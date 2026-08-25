using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Updates;

namespace OptiPaie.Services.Updates
{
    /// <summary>
    /// <see cref="IReleaseChannel"/> (and metadata source) backed by a simple, self-hosted
    /// <b>version.json</b> manifest — the simplest possible update source. One file, hosted
    /// anywhere (GitHub raw, GitHub Pages, any static host):
    /// <code>
    /// {
    ///   "latest_version": "1.15.0",
    ///   "download_url": "https://.../OptiPaie-PRO-Setup.exe",
    ///   "release_notes": "…",
    ///   "mandatory": false
    /// }
    /// </code>
    /// It reads the manifest, compares <c>latest_version</c> to the running PRODUCT version,
    /// downloads the installer with progress, then launches it and exits so it can update in
    /// place (the SQLite DB, settings, licence cache and modules live in %AppData% and are
    /// untouched). Offline / bad-JSON / HTTP errors degrade silently to "no update", so the
    /// app always opens. The <c>mandatory</c> flag and notes come from the same file.
    /// </summary>
    public sealed class VersionJsonReleaseChannel : IReleaseChannel, IUpdateMetadataSource
    {
        private static readonly HttpClient Http = CreateClient();

        private readonly UpdateOptions _options;
        private readonly ILogger _logger;

        private string _downloadUrl;
        private string _downloadedPath;
        private bool _mandatory;
        private string _releaseNotes = string.Empty;

        public VersionJsonReleaseChannel(UpdateOptions options, ILogger logger)
        {
            _options = Guard.AgainstNull(options, nameof(options));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        /// <summary>Enabled once a version.json URL is configured (blank in dev = disabled).</summary>
        public bool IsSupported => !string.IsNullOrWhiteSpace(_options.VersionJsonUrl);

        /// <summary>The installer URL parsed from the manifest — for the manual/browser fallback.</summary>
        public string DownloadUrl => _downloadUrl ?? string.Empty;

        public async Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            string current = AppVersion.CurrentProduct();
            if (!IsSupported)
            {
                return new ReleaseCheckResult { HasUpdate = false, CurrentVersion = current };
            }

            // Cache-bust so a freshly-edited manifest is picked up promptly.
            string url = _options.VersionJsonUrl.Trim();
            url += (url.IndexOf('?') >= 0 ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Info("version.json HTTP " + (int)response.StatusCode);
                    return new ReleaseCheckResult { HasUpdate = false, CurrentVersion = current };
                }

                VersionManifest m = ParseManifest(json);
                _downloadUrl = m.DownloadUrl;
                _mandatory = m.Mandatory;
                _releaseNotes = m.ReleaseNotes ?? string.Empty;

                return new ReleaseCheckResult
                {
                    // A manifest with a version + installer URL is a candidate; the pure
                    // UpdatePolicy decides whether it is actually newer (downgrade guard).
                    HasUpdate = !string.IsNullOrWhiteSpace(m.LatestVersion) && !string.IsNullOrWhiteSpace(m.DownloadUrl),
                    CurrentVersion = current,
                    LatestVersion = m.LatestVersion,
                    ReleaseNotes = _releaseNotes
                };
            }
        }

        /// <summary>The mandatory flag + notes come from the same version.json parsed in CheckAsync.</summary>
        public Task<UpdateMeta> GetForVersionAsync(string version, CancellationToken cancellationToken)
        {
            return Task.FromResult(new UpdateMeta { Mandatory = _mandatory, ReleaseNotes = _releaseNotes });
        }

        public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_downloadUrl))
            {
                throw new InvalidOperationException("No update installer URL to download.");
            }

            string dir = Path.Combine(Path.GetTempPath(), "OptiPaiePRO-Update");
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "OptiPaiePRO-Setup.exe");

            using (var request = new HttpRequestMessage(HttpMethod.Get, _downloadUrl))
            using (HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    long read = 0;
                    int n;
                    while ((n = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, n, cancellationToken).ConfigureAwait(false);
                        read += n;
                        if (progress != null && total.HasValue && total.Value > 0)
                        {
                            progress.Report((int)(read * 100 / total.Value));
                        }
                    }
                }
            }

            _downloadedPath = target;
            if (progress != null)
            {
                progress.Report(100);
            }
        }

        public void ApplyAndRestart()
        {
            // Detached, sequenced launch: the app fully exits and releases its files BEFORE the
            // visible installer starts, and the app reopens afterwards. See InstallerLauncher.
            InstallerLauncher.LaunchAndExit(_downloadedPath, _logger);
        }

        /// <summary>Pure parse of the version.json manifest — unit-tested, tolerant of key aliases.</summary>
        public static VersionManifest ParseManifest(string json)
        {
            var result = new VersionManifest();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            JObject root = JObject.Parse(json);
            result.LatestVersion = FirstString(root, "latest_version", "latestVersion", "version");
            result.DownloadUrl = FirstString(root, "download_url", "downloadUrl", "url");
            result.ReleaseNotes = FirstString(root, "release_notes", "releaseNotes", "notes") ?? string.Empty;

            JToken man = root["mandatory"] ?? root["required"];
            result.Mandatory = man != null &&
                (man.Type == JTokenType.Boolean
                    ? (bool)man
                    : string.Equals((string)man, "true", StringComparison.OrdinalIgnoreCase));

            return result;
        }

        private static string FirstString(JObject root, params string[] keys)
        {
            foreach (string k in keys)
            {
                JToken t = root[k];
                if (t != null && t.Type != JTokenType.Null)
                {
                    string s = (string)t;
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            return null;
        }

        private static HttpClient CreateClient()
        {
            // .NET Framework 4.8 defaults to SystemDefault TLS; force TLS 1.2 so the HTTPS download
            // works on machines whose OS/registry still negotiates only older protocols.
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; }
            catch { /* older frameworks may not expose Tls12 — the OS default then applies */ }

            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "OptiPaiePRO-Updater");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "no-cache");
            return client;
        }

        /// <summary>Parsed version.json manifest.</summary>
        public sealed class VersionManifest
        {
            public string LatestVersion { get; set; }
            public string DownloadUrl { get; set; }
            public string ReleaseNotes { get; set; }
            public bool Mandatory { get; set; }
        }
    }
}
