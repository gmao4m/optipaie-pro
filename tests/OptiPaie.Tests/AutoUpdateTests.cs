using NUnit.Framework;
using OptiPaie.Core.Updates;
using OptiPaie.Services.Updates;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The self-hosted version.json auto-update. Pins two things that silently break the
    /// feature: (1) the manifest must parse to the exact shape the client reads, and (2) the
    /// version comparison must offer ONLY a strictly-newer version — never re-prompt the
    /// running version, never downgrade. In particular the product version (1.14.0, from
    /// InformationalVersion) must read as newer than the frozen 1.8.0.0 AssemblyVersion.
    /// </summary>
    [TestFixture]
    public sealed class AutoUpdateTests
    {
        [Test]
        public void ParseManifest_ReadsTheDocumentedFormat()
        {
            const string json = @"{
              ""latest_version"": ""1.15.0"",
              ""download_url"": ""https://example.com/OptiPaie-PRO-Setup.exe"",
              ""release_notes"": ""Nouveautés"",
              ""mandatory"": true
            }";

            VersionJsonReleaseChannel.VersionManifest m = VersionJsonReleaseChannel.ParseManifest(json);

            Assert.That(m.LatestVersion, Is.EqualTo("1.15.0"));
            Assert.That(m.DownloadUrl, Is.EqualTo("https://example.com/OptiPaie-PRO-Setup.exe"));
            Assert.That(m.ReleaseNotes, Is.EqualTo("Nouveautés"));
            Assert.That(m.Mandatory, Is.True);
        }

        [Test]
        public void ParseManifest_MandatoryDefaultsFalse_AndToleratesMissingNotes()
        {
            VersionJsonReleaseChannel.VersionManifest m = VersionJsonReleaseChannel.ParseManifest(
                @"{ ""latest_version"": ""2.0.0"", ""download_url"": ""https://x/y.exe"" }");

            Assert.That(m.LatestVersion, Is.EqualTo("2.0.0"));
            Assert.That(m.Mandatory, Is.False);
            Assert.That(m.ReleaseNotes, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ParseManifest_MandatoryAcceptsStringTrue()
        {
            VersionJsonReleaseChannel.VersionManifest m = VersionJsonReleaseChannel.ParseManifest(
                @"{ ""latest_version"": ""2.0.0"", ""download_url"": ""https://x/y.exe"", ""mandatory"": ""true"" }");

            Assert.That(m.Mandatory, Is.True);
        }

        [Test]
        public void ParseManifest_EmptyOrNull_YieldsBlankManifest()
        {
            Assert.That(VersionJsonReleaseChannel.ParseManifest(null).LatestVersion, Is.Null);
            Assert.That(VersionJsonReleaseChannel.ParseManifest(string.Empty).LatestVersion, Is.Null);
        }

        [Test]
        public void Policy_OffersOnlyStrictlyNewerVersion()
        {
            // Newer → update available.
            AppUpdateCheck newer = UpdatePolicy.Evaluate("OptiPaie PRO", "1.14.0", "1.15.0", false, "notes");
            Assert.That(newer.UpdateAvailable, Is.True);
            Assert.That(newer.LatestVersion, Is.EqualTo("1.15.0"));

            // Same version → NO update (never re-prompt the running version).
            Assert.That(UpdatePolicy.Evaluate("OptiPaie PRO", "1.14.0", "1.14.0", false, "").UpdateAvailable, Is.False);

            // Older candidate → NO update (downgrade guard).
            Assert.That(UpdatePolicy.Evaluate("OptiPaie PRO", "1.14.0", "1.8.0", false, "").UpdateAvailable, Is.False);
        }

        /// <summary>
        /// Incident regression (2026-11-08): after a release, the server manifest was rolled
        /// BACK to the previous version as a safety net. A client already running the newer
        /// build must NEVER be offered the older version as an "update" (a silent downgrade).
        /// The comparison is strictly-greater ("newer than"), not "different from" — this test
        /// fails the instant anyone weakens it to an inequality.
        /// </summary>
        [Test]
        public void Policy_DoesNotOfferDowngrade_WhenServerManifestIsRolledBack()
        {
            // Client installed 1.40.1; server manifest announces 1.40.0 (rolled back).
            AppUpdateCheck rolledBack = UpdatePolicy.Evaluate("OptiPaie PRO", "1.40.1", "1.40.0", false, "notes");
            Assert.That(rolledBack.UpdateAvailable, Is.False,
                "A rolled-back server manifest must NOT offer the running client an older version.");

            // And the primitive the policy relies on agrees: 1.40.0 is NOT newer than 1.40.1.
            Assert.That(AppVersion.IsNewer("1.40.0", "1.40.1"), Is.False);
            // Equal manifest (client == server) is likewise not an update.
            Assert.That(AppVersion.IsNewer("1.40.1", "1.40.1"), Is.False);
            // Sanity: a genuine forward step is still offered.
            Assert.That(AppVersion.IsNewer("1.40.1", "1.40.0"), Is.True);
        }

        [Test]
        public void AppVersion_StripsInformationalBuildSuffix_AndComparesProductVsFrozen()
        {
            // InformationalVersion carries a "+commit" (SourceLink) suffix that must be stripped.
            Assert.That(AppVersion.TryParse("1.14.0+bdd472cbee1f", out AppVersion v), Is.True);
            Assert.That(v.ToString(), Is.EqualTo("1.14.0"));

            // The product version must read as NEWER than the frozen 1.8.0.0 AssemblyVersion,
            // otherwise every release would falsely appear as an update from "1.8.0.0".
            Assert.That(AppVersion.IsNewer("1.14.0", "1.8.0.0"), Is.True);
            Assert.That(AppVersion.IsNewer("1.15.0", "1.14.0"), Is.True);
            Assert.That(AppVersion.IsNewer("1.14.0", "1.14.0"), Is.False);
        }
    }
}
