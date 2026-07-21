using System;
using Newtonsoft.Json;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// Local, offline 48-hour trial. The state (start/expiry/last-seen) is stored
    /// encrypted (DPAPI) and guarded against clock-rollback: the effective time never
    /// goes below the highest time ever observed, so setting the clock back cannot
    /// extend the trial. A trial can be started only once per machine — every computer
    /// gets exactly one 48-hour evaluation with all modules unlocked.
    /// </summary>
    public sealed class TrialService : ITrialService
    {
        /// <summary>Trial length: 48 hours per computer.</summary>
        public const int TrialHours = 48;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.DateTime
        };

        private readonly ITrialStore _store;
        private readonly ILocalCipher _cipher;
        private readonly ILogger _logger;

        public TrialService(ITrialStore store, ILocalCipher cipher, ILogger logger)
        {
            _store = Guard.AgainstNull(store, nameof(store));
            _cipher = Guard.AgainstNull(cipher, nameof(cipher));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        public TrialInfo GetStatus()
        {
            Record record = Load();
            if (record == null)
            {
                return TrialInfo.NotStarted();
            }

            DateTime now = DateTime.UtcNow;
            DateTime effectiveNow = record.LastSeenUtc > now ? record.LastSeenUtc : now;

            if (effectiveNow > record.LastSeenUtc)
            {
                Save(record.StartedUtc, record.ExpiresUtc, effectiveNow);
            }

            return new TrialInfo(true, record.StartedUtc, record.ExpiresUtc, effectiveNow);
        }

        public TrialInfo StartTrial()
        {
            Record existing = Load();
            if (existing != null)
            {
                // Already started (possibly expired) — a trial cannot be restarted.
                return GetStatus();
            }

            DateTime now = DateTime.UtcNow;
            DateTime expires = now.AddHours(TrialHours);
            Save(now, expires, now);
            _logger.Info("Trial started; expires " + expires.ToString("o"));
            return new TrialInfo(true, now, expires, now);
        }

        private Record Load()
        {
            string blob;
            try
            {
                blob = _store.Load();
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not read trial state: " + ex.Message);
                return null;
            }

            if (string.IsNullOrEmpty(blob))
            {
                return null;
            }

            string json = _cipher.Unprotect(blob) ?? blob; // fallback for legacy plaintext
            try
            {
                Record record = JsonConvert.DeserializeObject<Record>(json, JsonSettings);
                return record != null && record.StartedUtc != default(DateTime) ? record : null;
            }
            catch
            {
                return null;
            }
        }

        private void Save(DateTime started, DateTime expires, DateTime lastSeen)
        {
            var record = new Record
            {
                StartedUtc = started.ToUniversalTime(),
                ExpiresUtc = expires.ToUniversalTime(),
                LastSeenUtc = lastSeen.ToUniversalTime()
            };

            try
            {
                string json = JsonConvert.SerializeObject(record, JsonSettings);
                string blob = _cipher.Protect(json) ?? json;
                _store.Save(blob);
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not persist trial state: " + ex.Message);
            }
        }

        private sealed class Record
        {
            public DateTime StartedUtc { get; set; }
            public DateTime ExpiresUtc { get; set; }
            public DateTime LastSeenUtc { get; set; }
        }
    }
}
