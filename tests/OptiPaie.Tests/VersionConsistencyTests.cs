using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using OptiPaie.Core.Updates;

namespace OptiPaie.Tests
{
    /// <summary>
    /// COMMIT L — the version trap. The shipped exe used to report FileVersion/AssemblyVersion
    /// 1.8.0.0 while the real product version was 1.40.x: anything reading FileVersion or
    /// GetName().Version concluded the install was OLDER than it was. FileVersion and AssemblyVersion
    /// now DERIVE from &lt;Version&gt; in Directory.Build.props. This test reads the compiled assembly
    /// and FAILS if those three fields ever diverge again (e.g. someone re-hardcodes a frozen value).
    /// </summary>
    [TestFixture]
    public sealed class VersionConsistencyTests
    {
        // Any product assembly carries the shared Directory.Build.props version attributes.
        private static readonly Assembly Product = typeof(AppVersion).Assembly; // OptiPaie.Core

        [Test]
        public void FileVersion_AssemblyVersion_And_InformationalVersion_AllAgree()
        {
            Version fileVersion = Version.Parse(FileVersionInfo.GetVersionInfo(Product.Location).FileVersion);
            Version assemblyVersion = Product.GetName().Version;
            Version informational = NumericOf(
                Product.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

            // Compare on major.minor.build (patch) — the three fields must name the SAME release.
            Assert.That(Triplet(fileVersion), Is.EqualTo(Triplet(informational)),
                "FileVersion (" + fileVersion + ") must match the product/InformationalVersion (" + informational + ")");
            Assert.That(Triplet(assemblyVersion), Is.EqualTo(Triplet(informational)),
                "AssemblyVersion (" + assemblyVersion + ") must match the product/InformationalVersion (" + informational + ")");
        }

        [Test]
        public void TheFrozenPlaceholder_IsGone()
        {
            // The specific trap: FileVersion/AssemblyVersion frozen at 1.8.0.0 while the product moved on.
            Assert.That(Triplet(Version.Parse(FileVersionInfo.GetVersionInfo(Product.Location).FileVersion)),
                Is.Not.EqualTo((1, 8, 0)), "FileVersion is frozen at the 1.8.0.0 placeholder again");
            Assert.That(Triplet(Product.GetName().Version), Is.Not.EqualTo((1, 8, 0)),
                "AssemblyVersion is frozen at the 1.8.0.0 placeholder again");
        }

        private static (int, int, int) Triplet(Version v) => (v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

        /// <summary>The numeric core of a semver string ("1.41.0+abc" / "1.41.0-pre" -> 1.41.0).</summary>
        private static Version NumericOf(string informational)
        {
            string s = (informational ?? string.Empty).Trim();
            int cut = s.IndexOfAny(new[] { '+', '-' });
            if (cut >= 0) s = s.Substring(0, cut);
            Version v = Version.Parse(s);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }
}
