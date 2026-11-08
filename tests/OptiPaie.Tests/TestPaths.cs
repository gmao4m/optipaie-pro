using System;
using System.IO;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Machine-independent paths for tests: the repository root is DISCOVERED by walking up from the
    /// test binary until the folder holding <c>OptiPaie.sln</c> / <c>.git</c> is found — never a
    /// hard-coded absolute path. Keeps the [Explicit] rendering harnesses runnable on any machine/CI.
    /// </summary>
    internal static class TestPaths
    {
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln")) ||
                    Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new InvalidOperationException(
                "Racine du dépôt introuvable (ni OptiPaie.sln ni .git au-dessus de " + AppContext.BaseDirectory + ").");
        }

        /// <summary>The ATS/DRT form templates folder (official form images + form-layout.json).</summary>
        public static string AtsDrtAssets() =>
            Path.Combine(RepoRoot(), "src", "OptiPaie.Desktop", "Assets", "AtsDrtTemplates");
    }
}
