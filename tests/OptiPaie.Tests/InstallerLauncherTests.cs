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

            // Waits on the exact PID before doing anything.
            Assert.That(s, Does.Contain("Get-Process -Id 1234"));

            int wait = s.IndexOf("Get-Process -Id 1234", System.StringComparison.Ordinal);
            int setup = s.IndexOf("Setup.exe' -Wait", System.StringComparison.Ordinal);
            int reopen = s.LastIndexOf("Start-Process -FilePath", System.StringComparison.Ordinal);

            Assert.That(wait, Is.GreaterThanOrEqualTo(0));
            Assert.That(setup, Is.GreaterThan(wait), "the installer runs only AFTER the wait-for-exit");
            Assert.That(reopen, Is.GreaterThan(setup), "the app is reopened AFTER the installer");
            Assert.That(s, Does.Contain("-Wait"), "the installer is launched visibly and waited on");
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
        public void Paths_WithApostrophes_AreEscapedForPowerShell()
        {
            string s = InstallerLauncher.BuildUpdateScript(1, @"C:\it's\setup.exe", @"C:\it's\app.exe");
            Assert.That(s, Does.Contain("C:\\it''s\\setup.exe"), "single quotes are doubled for a PS single-quoted literal");
        }
    }
}
