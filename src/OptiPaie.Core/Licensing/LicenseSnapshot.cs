using System;
using System.Collections.Generic;

namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// The effective local license state, as seen by the application. Derived from
    /// the verified signed token plus connectivity and time checks.
    /// </summary>
    public enum LicenseStateKind
    {
        /// <summary>No license has been activated on this machine.</summary>
        NotActivated = 0,

        /// <summary>A valid, active license — the app and its modules are usable.</summary>
        Active = 1,

        /// <summary>The license was suspended by the vendor.</summary>
        Suspended = 2,

        /// <summary>The license was revoked by the vendor.</summary>
        Revoked = 3,

        /// <summary>A dated license whose expiry has passed.</summary>
        Expired = 4,

        /// <summary>Offline too long: the offline grace window elapsed without revalidation.</summary>
        GraceExpired = 5,

        /// <summary>The stored license failed verification (tampered, wrong device or product).</summary>
        Invalid = 6
    }

    /// <summary>
    /// An immutable snapshot of the current license and the modules it unlocks.
    /// Everything the UI needs to gate features and show status.
    /// </summary>
    public sealed class LicenseSnapshot
    {
        private readonly HashSet<string> _modules;

        public LicenseSnapshot(
            LicenseStateKind state,
            string productKey,
            string licenseKey,
            string companyName,
            string email,
            string deviceId,
            string serverStatus,
            IEnumerable<string> modules,
            DateTime? activatedAtUtc,
            DateTime? lastValidationUtc,
            DateTime? expiresAtUtc,
            DateTime? graceUntilUtc,
            LicenseType type = LicenseType.Unknown,
            string customerName = null)
        {
            State = state;
            ProductKey = productKey ?? string.Empty;
            LicenseKey = licenseKey ?? string.Empty;
            CompanyName = companyName ?? string.Empty;
            CustomerName = string.IsNullOrWhiteSpace(customerName) ? (companyName ?? string.Empty) : customerName;
            Email = email ?? string.Empty;
            DeviceId = deviceId ?? string.Empty;
            ServerStatus = serverStatus ?? string.Empty;
            Type = type;
            _modules = new HashSet<string>(modules ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            ActivatedAtUtc = activatedAtUtc;
            LastValidationUtc = lastValidationUtc;
            ExpiresAtUtc = expiresAtUtc;
            GraceUntilUtc = graceUntilUtc;
        }

        public LicenseStateKind State { get; }
        public string ProductKey { get; }
        public string LicenseKey { get; }
        public string CompanyName { get; }

        /// <summary>Customer display name (falls back to the company name).</summary>
        public string CustomerName { get; }

        public string Email { get; }
        public string DeviceId { get; }

        /// <summary>The commercial license type.</summary>
        public LicenseType Type { get; }

        /// <summary>Whole days until expiry (null for a perpetual license, 0 when expired).</summary>
        public int? DaysRemaining
        {
            get
            {
                if (!ExpiresAtUtc.HasValue)
                {
                    return null;
                }

                double days = (ExpiresAtUtc.Value - DateTime.UtcNow).TotalDays;
                return days <= 0 ? 0 : (int)System.Math.Ceiling(days);
            }
        }

        /// <summary>The raw server status carried by the token (active/suspended/revoked).</summary>
        public string ServerStatus { get; }

        public DateTime? ActivatedAtUtc { get; }
        public DateTime? LastValidationUtc { get; }
        public DateTime? ExpiresAtUtc { get; }
        public DateTime? GraceUntilUtc { get; }

        /// <summary>The enabled module keys (only meaningful when <see cref="IsUsable"/>).</summary>
        public IReadOnlyCollection<string> Modules => _modules;

        /// <summary>True once a license exists on this machine and verifies as ours.</summary>
        public bool IsActivated =>
            State != LicenseStateKind.NotActivated && State != LicenseStateKind.Invalid;

        /// <summary>True when the app and its licensed modules may be used right now.</summary>
        public bool IsUsable => State == LicenseStateKind.Active;

        /// <summary>True when the given module is currently unlocked.</summary>
        public bool IsModuleEnabled(string moduleKey)
        {
            return IsUsable && !string.IsNullOrEmpty(moduleKey) && _modules.Contains(moduleKey);
        }

        /// <summary>A snapshot representing "no license yet".</summary>
        public static LicenseSnapshot NotActivated()
        {
            return new LicenseSnapshot(
                LicenseStateKind.NotActivated,
                null, null, null, null, null, null, null, null, null, null, null);
        }
    }
}
