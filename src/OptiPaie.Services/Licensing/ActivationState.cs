using System;
using System.Globalization;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// <see cref="IActivationState"/> stored in the local settings key/value table — a
    /// plain, DPAPI-independent flag, so it survives the very failures (unreadable encrypted
    /// cache, device-id drift) that used to lock out an already-activated customer.
    /// </summary>
    public sealed class ActivationState : IActivationState
    {
        private const string KeyCompleted = "Activation.Completed";
        private const string KeyCompletedAt = "Activation.CompletedAtUtc";

        private readonly ISettingsService _settings;

        public ActivationState(ISettingsService settings)
        {
            _settings = Guard.AgainstNull(settings, nameof(settings));
        }

        public bool IsActivated
        {
            get
            {
                try { return _settings.Get(KeyCompleted, null) == "1"; }
                catch { return false; }
            }
        }

        public void MarkActivated()
        {
            try
            {
                if (_settings.Get(KeyCompleted, null) == "1")
                {
                    return; // already recorded
                }

                _settings.Set(KeyCompleted, "1");
                _settings.Set(KeyCompletedAt, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            }
            catch
            {
                // Best-effort: if we cannot persist the marker, the license cache still gates
                // the app, so nothing is weakened — the customer may just be asked once more.
            }
        }
    }
}
