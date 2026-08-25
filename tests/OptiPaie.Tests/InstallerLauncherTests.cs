using NUnit.Framework;
using OptiPaie.Services.Updates;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The detached update/repair launcher scripts. Guards the sequence that the 1.29.0 crash
    /// required: the app must fully EXIT before the installer runs (so its files are released),
    /// and the app must be reopened AFTER — so a cancelled/failed install never leaves the user
    /// with a disappeared app. (The end-to-end timing is proven separately in a harness.)
    /// </summary>
    [TestFixture]
    public sealed class InstallerLauncherTests
    {
        [Test]
        public void UpdateScript_WaitsForTheApp_ThenRunsSetup_ThenReopens_InThatOrder()
        {
            string s = InstallerLauncher.BuildUpdateScript(1234, @"C:\Temp\OptiPaie PRO Setup.exe", @"C:\Program Files\OptiPaie PRO\OptiPaie PRO.exe");

            // The update helper is a cmd batch, NEVER PowerShell (client GPO blocks PS execution).
            Assert.That(s, Does.Contain("@echo off"), "the update helper is a cmd batch");
            Assert.That(s.ToLowerInvariant(), Does.Not.Contain("powershell"), "the update helper must not use PowerShell");
            Assert.That(s, Does.Contain("PID eq 1234"), "waits on the exact PID before doing anything");

            int wait = s.IndexOf("PID eq 1234", System.StringComparison.Ordinal);
            int setup = s.IndexOf("/wait \"", System.StringComparison.Ordinal);
            int reopen = s.LastIndexOf("start \"\" \"", System.StringComparison.Ordinal);

            Assert.That(wait, Is.GreaterThanOrEqualTo(0));
            Assert.That(setup, Is.GreaterThan(wait), "the installer runs only AFTER the wait-for-exit");
            Assert.That(reopen, Is.GreaterThan(setup), "the app is reopened AFTER the installer");
            Assert.That(s, Does.Contain("/wait"), "the installer is launched visibly and waited on");
        }

        [Test]
        public void RepairScript_WaitsForTheApp_ThenElevatedRepair_ThenReopens()
        {
            string s = InstallerLauncher.BuildRepairScript(999, "{PRODUCT-CODE}", @"C:\Program Files\OptiPaie PRO\OptiPaie PRO.exe");

            Assert.That(s, Does.Contain("Get-Process -Id 999"));
            Assert.That(s, Does.Contain("msiexec"));
            Assert.That(s, Does.Contain("/fomus {PRODUCT-CODE}"), "repairs missing files from the Installer cache");
            Assert.That(s, Does.Contain("-Verb RunAs"), "the repair is elevated");

            int repair = s.IndexOf("msiexec", System.StringComparison.Ordinal);
            int reopen = s.LastIndexOf("Start-Process -FilePath", System.StringComparison.Ordinal);
            Assert.That(reopen, Is.GreaterThan(repair), "the app is reopened AFTER the repair");
        }

        [Test]
        public void UpdatePaths_WithPercent_AreEscapedForCmd()
        {
            string s = InstallerLauncher.BuildUpdateScript(1, @"C:\100%\setup.exe", @"C:\100%\app.exe");
            Assert.That(s, Does.Contain(@"C:\100%%\setup.exe"), "percent is doubled so cmd does not treat it as a variable");
        }
    }
}
