using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OptiPaie.Common.Logging;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The login/activation path now writes every failure to the persistent, timestamped
    /// file log (not only to the screen). This proves the log mechanism the ViewModel uses:
    /// entries survive on disk and carry a timestamp, so a customer's failure is diagnosable
    /// after the fact. (The ViewModel calls this same logger on each failure branch.)
    /// </summary>
    [TestFixture]
    public sealed class LoginLogTests
    {
        [Test]
        public void FileLogger_PersistsTimestampedFailureEntries()
        {
            string dir = Path.Combine(Path.GetTempPath(), "optipaie-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "app.log");
            try
            {
                var logger = new FileLogger(file);

                logger.Warn("Login failed: wrong password for 'user@sarl.dz'.");
                logger.Error("Login failed unexpectedly for 'user@sarl.dz'.", new InvalidOperationException("boom"));

                Assert.That(File.Exists(file), Is.True, "the log file is created and persisted on disk");

                string content = File.ReadAllText(file);
                Assert.That(content, Does.Contain("Login failed: wrong password"), "the failure message is recorded");
                Assert.That(content, Does.Contain("boom"), "the exception detail is recorded, not swallowed");
                Assert.That(Regex.IsMatch(content, @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}"), Is.True,
                    "each entry carries a timestamp");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }
    }
}
