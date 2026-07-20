using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Updates;

namespace OptiPaie.Services.Updates
{
    /// <summary>
    /// Orchestrates auto-update: asks the release channel for the latest version,
    /// resolves the mandatory flag + notes from the metadata source, and applies the
    /// pure <see cref="UpdatePolicy"/>. Handles offline/download failures gracefully.
    /// Free of any Velopack dependency — the channel is injected — so it is fully
    /// unit-testable with fakes.
    /// </summary>
    public sealed class UpdateService : IUpdateService
    {
        private readonly IReleaseChannel _channel;
        private readonly IUpdateMetadataSource _metadata;
        private readonly UpdateOptions _options;
        private readonly ILogger _logger;

        private DateTime? _lastCheckUtc;

        public UpdateService(IReleaseChannel channel, IUpdateMetadataSource metadata, UpdateOptions options, ILogger logger)
        {
            _channel = Guard.AgainstNull(channel, nameof(channel));
            _metadata = Guard.AgainstNull(metadata, nameof(metadata));
            _options = Guard.AgainstNull(options, nameof(options));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        public bool IsSupported => _channel.IsSupported && _options.IsConfigured;

        public DateTime? LastCheckUtc => _lastCheckUtc;

        public async Task<AppUpdateCheck> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            if (!IsSupported)
            {
                return AppUpdateCheck.None(_options.AppName, CurrentVersionSafe());
            }

            try
            {
                ReleaseCheckResult check = await _channel.CheckAsync(cancellationToken).ConfigureAwait(false);
                _lastCheckUtc = DateTime.UtcNow;

                if (check == null || !check.HasUpdate)
                {
                    return AppUpdateCheck.None(_options.AppName, check != null ? check.CurrentVersion : CurrentVersionSafe());
                }

                UpdateMeta meta;
                try
                {
                    meta = await _metadata.GetForVersionAsync(check.LatestVersion, cancellationToken).ConfigureAwait(false)
                           ?? UpdateMeta.Default;
                }
                catch (Exception ex)
                {
                    _logger.Warn("Update metadata unavailable: " + ex.Message);
                    meta = UpdateMeta.Default;
                }

                // Prefer the notes carried by the channel (e.g. the GitHub release body);
                // fall back to the admin-managed metadata notes.
                string notes = !string.IsNullOrWhiteSpace(check.ReleaseNotes)
                    ? check.ReleaseNotes
                    : meta.ReleaseNotes;

                _logger.Info("Update check: current=" + check.CurrentVersion + " latest=" + check.LatestVersion +
                             " mandatory=" + meta.Mandatory);

                return UpdatePolicy.Evaluate(_options.AppName, check.CurrentVersion, check.LatestVersion,
                    meta.Mandatory, notes);
            }
            catch (Exception ex)
            {
                // Offline / transient — never interrupt the user.
                _logger.Info("Update check skipped: " + ex.Message);
                return AppUpdateCheck.None(_options.AppName, CurrentVersionSafe());
            }
        }

        public async Task<UpdateApplyResult> DownloadAndApplyAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            if (!IsSupported)
            {
                return UpdateApplyResult.Fail("Les mises à jour ne sont pas disponibles pour cette installation.");
            }

            try
            {
                await _channel.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);
                _logger.Info("Update downloaded; applying and restarting.");
                _channel.ApplyAndRestart(); // relaunches into the new version
                return UpdateApplyResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return UpdateApplyResult.Fail("Téléchargement annulé.");
            }
            catch (Exception ex)
            {
                _logger.Error("Update download/apply failed.", ex);
                return UpdateApplyResult.Fail(
                    "Le téléchargement de la mise à jour a échoué. Vérifiez votre connexion et réessayez.");
            }
        }

        private static string CurrentVersionSafe()
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
    }
}
