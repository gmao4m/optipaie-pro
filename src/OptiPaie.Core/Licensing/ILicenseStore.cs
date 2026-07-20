using System;
using System.Collections.Generic;

namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// The locally persisted license row (a single-row cache). Dates are stored as
    /// ISO-8601 UTC strings to stay culture-safe; the licensing service converts to
    /// and from <see cref="DateTime"/>. The signed token is the source of truth; the
    /// other fields are a convenience mirror for quick display.
    /// </summary>
    public sealed class StoredLicense
    {
        public string ProductKey { get; set; }
        public string LicenseKey { get; set; }
        public string CompanyName { get; set; }
        public string Email { get; set; }
        public string DeviceId { get; set; }
        public string Status { get; set; }

        /// <summary>The full signed token; re-verified on every load.</summary>
        public string SignedToken { get; set; }

        public string ActivatedAtUtc { get; set; }
        public string LastValidationUtc { get; set; }
        public string ExpiresAtUtc { get; set; }
        public string GraceUntilUtc { get; set; }

        /// <summary>Highest UTC time ever observed — anti clock-rollback guard.</summary>
        public string LastSeenUtc { get; set; }
    }

    /// <summary>
    /// Persists the local license cache in the shared SQLite database. Completely
    /// separate from the payroll/business repositories.
    /// </summary>
    public interface ILicenseStore
    {
        /// <summary>Loads the stored license, or null when none is present.</summary>
        StoredLicense Load();

        /// <summary>Loads the cached enabled module keys (for display without re-verifying).</summary>
        IReadOnlyList<string> LoadModules();

        /// <summary>Saves the license row and refreshes the cached module list atomically.</summary>
        void Save(StoredLicense license, IEnumerable<string> enabledModuleKeys);

        /// <summary>Updates only the anti-rollback last-seen timestamp (ISO-8601 UTC).</summary>
        void UpdateLastSeen(string lastSeenUtcIso);

        /// <summary>Removes any stored license (used when the app is de-activated).</summary>
        void Clear();
    }
}
