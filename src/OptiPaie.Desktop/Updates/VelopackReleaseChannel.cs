using System;
using System.Threading;
using System.Threading.Tasks;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Updates;

namespace OptiPaie.Desktop.Updates
{
    /// <summary>
    /// <see cref="IReleaseChannel"/> backed by Velopack. The only class that references
    /// Velopack — everything else depends on the interface, keeping the update policy
    /// and orchestration testable. Self-update is possible only when the app is running
    /// as a Velopack-installed build (so dev/bin runs simply report "not supported").
    /// </summary>
    public sealed class VelopackReleaseChannel : IReleaseChannel
    {
        private readonly UpdateOptions _options;
        private readonly ILogger _logger;

        private Velopack.UpdateManager _manager;
        private Velopack.UpdateInfo _pending;
        private bool _initialised;

        public VelopackReleaseChannel(UpdateOptions options, ILogger logger)
        {
            _options = options;
            _logger = logger;
        }

        private Velopack.UpdateManager Manager()
        {
            if (!_initialised)
            {
                _initialised = true;
                try
                {
                    if (_options != null && _options.IsConfigured)
                    {
                        _manager = new Velopack.UpdateManager(_options.FeedUrl);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("Velopack initialisation failed: " + ex.Message);
                    _manager = null;
                }
            }

            return _manager;
        }

        public bool IsSupported
        {
            get
            {
                Velopack.UpdateManager m = Manager();
                try { return m != null && m.IsInstalled; }
                catch { return false; }
            }
        }

        public async Task<ReleaseCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            Velopack.UpdateManager m = Manager();
            if (m == null || !m.IsInstalled)
            {
                return new ReleaseCheckResult { HasUpdate = false, CurrentVersion = CurrentVersion(m) };
            }

            Velopack.UpdateInfo info = await m.CheckForUpdatesAsync().ConfigureAwait(false);
            _pending = info;

            return new ReleaseCheckResult
            {
                HasUpdate = info != null,
                CurrentVersion = CurrentVersion(m),
                LatestVersion = info != null && info.TargetFullRelease != null
                    ? info.TargetFullRelease.Version.ToString()
                    : null
            };
        }

        public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            Velopack.UpdateManager m = Manager();
            if (m == null || _pending == null)
            {
                throw new InvalidOperationException("No pending update to download.");
            }

            await m.DownloadUpdatesAsync(_pending, p => { if (progress != null) progress.Report(p); }, cancellationToken)
                .ConfigureAwait(false);
        }

        public void ApplyAndRestart()
        {
            Velopack.UpdateManager m = Manager();
            if (m == null || _pending == null)
            {
                throw new InvalidOperationException("No pending update to apply.");
            }

            m.ApplyUpdatesAndRestart(_pending);
        }

        private static string CurrentVersion(Velopack.UpdateManager m)
        {
            try { return m != null && m.CurrentVersion != null ? m.CurrentVersion.ToString() : null; }
            catch { return null; }
        }
    }
}
