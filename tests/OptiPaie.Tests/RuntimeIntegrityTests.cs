using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Common.Diagnostics;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The startup integrity check that detects an INCOMPLETE install (a critical runtime
    /// assembly missing next to the exe — the 1.29.0 Newtonsoft.Json class of failure) so the
    /// app can show a clear message instead of a cryptic crash.
    /// </summary>
    [TestFixture]
    public sealed class RuntimeIntegrityTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-integrity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void AllCriticalFilesPresent_ReturnsEmpty()
        {
            foreach (string f in RuntimeIntegrity.CriticalRuntimeFiles)
            {
                File.WriteAllText(Path.Combine(_dir, f), "x");
            }

            Assert.That(RuntimeIntegrity.MissingCriticalFiles(_dir), Is.Empty, "a complete install has nothing missing");
        }

        [Test]
        public void OneMissing_IsReportedByName()
        {
            // Everything present except Newtonsoft.Json.dll (the exact 1.29.0 failure).
            foreach (string f in RuntimeIntegrity.CriticalRuntimeFiles)
            {
                if (f != "Newtonsoft.Json.dll") File.WriteAllText(Path.Combine(_dir, f), "x");
            }

            var missing = RuntimeIntegrity.MissingCriticalFiles(_dir);
            Assert.That(missing, Does.Contain("Newtonsoft.Json.dll"));
            Assert.That(missing.Count, Is.EqualTo(1));
        }

        [Test]
        public void AllMissing_AreAllReported()
        {
            var missing = RuntimeIntegrity.MissingCriticalFiles(_dir); // empty folder
            Assert.That(missing.Count, Is.EqualTo(RuntimeIntegrity.CriticalRuntimeFiles.Count));
        }

        [Test]
        public void NullOrBlankBaseDir_DoesNotThrow_ReturnsEmpty()
        {
            Assert.That(RuntimeIntegrity.MissingCriticalFiles(null), Is.Empty);
            Assert.That(RuntimeIntegrity.MissingCriticalFiles("   "), Is.Empty);
        }

        [Test]
        public void NewtonsoftIsOnTheCriticalList()
        {
            // Regression guard: the assembly whose absence caused the 1.29.0 startup crash must
            // stay on the list so this failure mode is always caught early with a clear message.
            Assert.That(RuntimeIntegrity.CriticalRuntimeFiles, Does.Contain("Newtonsoft.Json.dll"));
        }
    }
}
