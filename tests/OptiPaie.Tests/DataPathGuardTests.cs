using System;
using NUnit.Framework;
using OptiPaie.Common.Configuration;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Pins the shared-folder guard: the app must refuse a data directory on a UNC path or a
    /// network drive, because an SQLite database opened from two machines over a share is silently
    /// corrupted. A local path (the default %AppData%) must pass untouched so no existing customer
    /// is affected. Fails the instant the guard is weakened or removed.
    /// </summary>
    [TestFixture]
    public sealed class DataPathGuardTests
    {
        [TestCase(@"\\SERVEUR\partage\OptiPaie")]
        [TestCase(@"\\192.168.1.10\data\optipaie")]
        [TestCase("//server/share/optipaie")]
        public void IsNetworkPath_TrueForUncPaths(string path)
        {
            Assert.That(DataPathGuard.IsNetworkPath(path), Is.True);
        }

        [Test]
        public void IsNetworkPath_FalseForLocalAppDataDefault()
        {
            string local = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OptiPaie DZ");
            Assert.That(DataPathGuard.IsNetworkPath(local), Is.False);
        }

        [TestCase(@"C:\Users\x\AppData\Roaming\OptiPaie DZ")]
        [TestCase(@"C:\OptiPaie")]
        public void IsNetworkPath_FalseForLocalDrive(string path)
        {
            Assert.That(DataPathGuard.IsNetworkPath(path), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsNetworkPath_FalseForBlank(string path)
        {
            Assert.That(DataPathGuard.IsNetworkPath(path), Is.False);
        }

        [Test]
        public void Validate_ThrowsOnUnc_WithBilingualMessage()
        {
            DataPathException ex = Assert.Throws<DataPathException>(
                () => DataPathGuard.Validate(@"\\SERVEUR\partage\OptiPaie"));

            // Bilingual + non-technical: carries both the Arabic and the French warning.
            Assert.That(ex.Message, Does.Contain("قاعدة بيانات OptiPaie"));
            Assert.That(ex.Message, Does.Contain("La base de données OptiPaie"));
        }

        [Test]
        public void Validate_DoesNotThrowOnLocalPath()
        {
            string local = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OptiPaie DZ");
            Assert.DoesNotThrow(() => DataPathGuard.Validate(local));
        }
    }
}
