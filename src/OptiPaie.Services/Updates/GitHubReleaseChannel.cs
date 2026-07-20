using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Updates;

namespace OptiPaie.Services.Updates
{
    /// <summary>
    /// <see cref="IReleaseChannel"/> that checks the latest <b>GitHub Release</b> of a
    /// public repository: compares the release tag to the running version, downloads the
    /// Setup.exe asset with progress, then launches it and exits so the installer can
    /// update in place (the local SQLite database, settings, license cache and activated
    /// modules live in %AppData% and are untouched). No token needed for public repos.
    /// </summary>
    public sealed class GitHubReleaseChannel : IReleaseChannel
    {
        private static readonly HttpClient Http = CreateClient();

        private readonly UpdateOptions _options;
        private readonly ILogger _logger;

        private string _downloadUrl;
        private string _downloadedPath;

        public GitHubReleaseChannel(UpdateOptions options, ILogger logger)
        {
            _options = Guard.AgainstNull(options, nameof(options));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        /// <summary>Enabled once a GitHub repo is configured (blank in dev = disabled).</summary>
        public bool IsSupported => !string.IsNullOrWhiteSpace(_options.GitHubRepo);

        public async Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            string current = CurrentVersion();
            if (!IsSupported)
            {
                return new ReleaseCheckResult { HasUpdate = false, CurrentVersion = current };
            }

            string url = "https://api.github.com/repos/" + _options.GitHubRepo.Trim('/') + "/releases/latest";
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                using (HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Info("GitHub releases HTTP " + (int)response.StatusCode);
                        return new ReleaseCheckResult { HasUpdate = false, CurrentVersion = current };
                    }

                    GitHubRelease release = ParseLatestRelease(json);
                    _downloadUrl = release.SetupAssetUrl;

                    return new ReleaseCheckResult
                    {
                        // Any published release with a Setup asset is a candidate; the pure
                        // UpdatePolicy decides whether the version is actually newer.
                        HasUpdate = release.Version != null && !string.IsNullOrEmpty(release.SetupAssetUrl),
                        CurrentVersion = current,
                        LatestVersion = release.Version,
                        ReleaseNotes = release.ReleaseNotes
                    };
                }
            }
        }

        public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_downloadUrl))
            {
                throw new InvalidOperationException("No update asset to download.");
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
            if (string.IsNullOrEmpty(_downloadedPath) || !File.Exists(_downloadedPath))
            {
                throw new InvalidOperationException("No downloaded installer to launch.");
            }

            // Launch the installer, then exit so it can replace the running binaries.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_downloadedPath) { UseShellExecute = true });
            Environment.Exit(0);
        }

        /// <summary>Pure parse of GitHub's /releases/latest JSON — unit-tested.</summary>
        public static GitHubRelease ParseLatestRelease(string json)
        {
            var result = new GitHubRelease();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            JObject root = JObject.Parse(json);
            result.Version = NormalizeTag((string)root["tag_name"]);
            result.ReleaseNotes = (string)root["body"] ?? string.Empty;

            var assets = root["assets"] as JArray;
            if (assets != null)
            {
                string firstExe = null;
                foreach (JToken asset in assets)
                {
                    string name = (string)asset["name"] ?? string.Empty;
                    string dl = (string)asset["browser_download_url"];
                    if (string.IsNullOrEmpty(dl) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (firstExe == null)
                    {
                        firstExe = dl;
                    }

                    // Prefer a Setup*.exe asset.
                    if (name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.SetupAssetUrl = dl;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(result.SetupAssetUrl))
                {
                    result.SetupAssetUrl = firstExe;
                }
            }

            return result;
        }

        private static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }

            string t = tag.Trim();
            if (t.Length > 0 && (t[0] == 'v' || t[0] == 'V'))
            {
                t = t.Substring(1);
            }

            return t;
        }

        private static string CurrentVersion()
        {
            try
            {
                Version v = Assembly.GetEntryAssembly() != null ? Assembly.GetEntryAssembly().GetName().Version : null;
                return v != null ? v.ToString(3) : "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            // GitHub requires a User-Agent header on API requests.
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "OptiPaiePRO-Updater");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            return client;
        }

        /// <summary>Parsed GitHub release info.</summary>
        public sealed class GitHubRelease
        {
            public string Version { get; set; }
            public string ReleaseNotes { get; set; }
            public string SetupAssetUrl { get; set; }
        }
    }
}
