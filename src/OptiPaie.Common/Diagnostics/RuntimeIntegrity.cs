using System.Collections.Generic;
using System.IO;

namespace OptiPaie.Common.Diagnostics
{
    /// <summary>
    /// Fail-fast check that the deployed application folder actually contains the runtime
    /// assemblies the app loads at startup. Motivation (1.29.0 incident): an interrupted
    /// install/update can leave the folder missing a required DLL (e.g. Newtonsoft.Json.dll,
    /// which licensing loads at launch), and the app then crashed with a cryptic
    /// "Erreur d'initialisation". Detecting the specific missing file lets the host show a
    /// clear message and self-repair instead. Pure and unit-testable.
    /// </summary>
    public static class RuntimeIntegrity
    {
        /// <summary>
        /// Managed assemblies that MUST sit next to the executable — their absence faults the
        /// app during startup (licensing / data). Native interops (SQLite.Interop.dll) live in
        /// bitness subfolders and are validated at build time, not here.
        /// </summary>
        public static readonly IReadOnlyList<string> CriticalRuntimeFiles = new[]
        {
            "Newtonsoft.Json.dll",     // licensing (LicenseService / Ed25519 / Trial / Supabase) + updater
            "System.Data.SQLite.dll",  // every database read at startup
        };

        /// <summary>
        /// Returns the critical runtime files that are MISSING from <paramref name="baseDirectory"/>.
        /// An empty list means the install is complete. Never throws.
        /// </summary>
        public static IReadOnlyList<string> MissingCriticalFiles(string baseDirectory)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return missing;
            }

            foreach (string file in CriticalRuntimeFiles)
            {
                try
                {
                    if (!File.Exists(Path.Combine(baseDirectory, file)))
                    {
                        missing.Add(file);
                    }
                }
                catch
                {
                    // A probe failure must not itself crash startup; treat as present.
                }
            }

            return missing;
        }
    }
}
