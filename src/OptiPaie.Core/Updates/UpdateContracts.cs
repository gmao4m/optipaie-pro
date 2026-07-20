using System;
using System.Threading;
using System.Threading.Tasks;

namespace OptiPaie.Core.Updates
{
    /// <summary>The result of an update check, shown by the update dialog.</summary>
    public sealed class AppUpdateCheck
    {
        public AppUpdateCheck(bool updateAvailable, string appName, string currentVersion,
            string latestVersion, string releaseNotes, bool mandatory)
        {
            UpdateAvailable = updateAvailable;
            AppName = appName ?? string.Empty;
            CurrentVersion = currentVersion ?? string.Empty;
            LatestVersion = latestVersion ?? string.Empty;
            ReleaseNotes = releaseNotes ?? string.Empty;
            Mandatory = mandatory;
        }

        public bool UpdateAvailable { get; }
        public string AppName { get; }
        public string CurrentVersion { get; }
        public string LatestVersion { get; }
        public string ReleaseNotes { get; }
        public bool Mandatory { get; }

        public static AppUpdateCheck None(string appName, string currentVersion)
        {
            return new AppUpdateCheck(false, appName, currentVersion, currentVersion, string.Empty, false);
        }
    }

    /// <summary>Outcome of downloading and applying an update.</summary>
    public sealed class UpdateApplyResult
    {
        private UpdateApplyResult(bool success, string error)
        {
            Success = success;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public string Error { get; }

        public static UpdateApplyResult Ok() => new UpdateApplyResult(true, string.Empty);
        public static UpdateApplyResult Fail(string error) => new UpdateApplyResult(false, error);
    }

    /// <summary>What the release channel reports about the latest available release.</summary>
    public sealed class ReleaseCheckResult
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }

        /// <summary>Release notes carried by the channel (e.g. the GitHub release body); optional.</summary>
        public string ReleaseNotes { get; set; }
    }

    /// <summary>Per-version update metadata (mandatory flag, notes) from the admin backend.</summary>
    public sealed class UpdateMeta
    {
        public bool Mandatory { get; set; }
        public string ReleaseNotes { get; set; }

        public static UpdateMeta Default => new UpdateMeta { Mandatory = false, ReleaseNotes = string.Empty };
    }

    /// <summary>
    /// Abstracts the update transport (Velopack). Kept separate so the update policy
    /// and orchestration can be tested with a fake channel — no network required.
    /// </summary>
    public interface IReleaseChannel
    {
        /// <summary>True only when the app can actually self-update (installed + feed configured).</summary>
        bool IsSupported { get; }

        Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken);

        /// <summary>Downloads the pending update, reporting 0..100. Throws on failure.</summary>
        Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken);

        /// <summary>Applies the downloaded update and restarts into the new version.</summary>
        void ApplyAndRestart();
    }

    /// <summary>Reads per-version metadata (mandatory, notes) from the backend.</summary>
    public interface IUpdateMetadataSource
    {
        Task<UpdateMeta> GetForVersionAsync(string version, CancellationToken cancellationToken);
    }

    /// <summary>The application-facing auto-update service.</summary>
    public interface IUpdateService
    {
        /// <summary>True when self-update is possible in this build/environment.</summary>
        bool IsSupported { get; }

        /// <summary>UTC time of the last completed check, or null.</summary>
        DateTime? LastCheckUtc { get; }

        Task<AppUpdateCheck> CheckForUpdatesAsync(CancellationToken cancellationToken);

        Task<UpdateApplyResult> DownloadAndApplyAsync(IProgress<int> progress, CancellationToken cancellationToken);
    }

    /// <summary>Configuration for the updater (from appsettings — no hardcoded URLs).</summary>
    public sealed class UpdateOptions
    {
        public string AppName { get; set; } = "OptiPaie PRO";

        /// <summary>GitHub "owner/repo" whose Releases host the installer. Preferred source.</summary>
        public string GitHubRepo { get; set; }

        /// <summary>Velopack feed URL (RELEASES + packages) — used when no GitHub repo is set.</summary>
        public string FeedUrl { get; set; }

        /// <summary>REST endpoint of the `updates` table (for the mandatory flag + notes).</summary>
        public string MetadataUrl { get; set; }

        /// <summary>Publishable/anon key for the metadata read.</summary>
        public string AnonKey { get; set; }

        /// <summary>True when at least one update source (GitHub or Velopack feed) is configured.</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(GitHubRepo) || !string.IsNullOrWhiteSpace(FeedUrl);
    }
}
