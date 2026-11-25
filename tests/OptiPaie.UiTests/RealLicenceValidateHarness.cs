using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OptiPaie.Core.Licensing;
using OptiPaie.Data.Context;
using OptiPaie.Data.Licensing;
using OptiPaie.Services.Licensing;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] One REAL licence check against the PRODUCTION validate endpoint, by the REAL client
    /// pipeline (SupabaseLicenseBackend → Ed25519LicenseVerifier → LicensingService → SqliteLicenseStore),
    /// on a COPY of this PC's database (REAL_VALIDATE_DB). Endpoint + public key are read from the shipped
    /// OptiPaie PRO.exe.config (REAL_VALIDATE_CONFIG). It is exactly the check the app makes at startup;
    /// the backend refreshes this device's heartbeat as it does every day. Prints the licence type that
    /// arrives. Licence key and e-mail are masked in the output.
    /// </summary>
    [TestFixture, Explicit]
    public sealed class RealLicenceValidateHarness
    {
        [Test]
        public void Real_Validate_Against_Production()
        {
            string db = Environment.GetEnvironmentVariable("REAL_VALIDATE_DB");
            string configPath = Environment.GetEnvironmentVariable("REAL_VALIDATE_CONFIG");
            string appVersion = Environment.GetEnvironmentVariable("REAL_VALIDATE_APPVERSION") ?? "1.43.0";
            Assert.That(File.Exists(db) && File.Exists(configPath), Is.True);
            Assert.That(db.IndexOf(@"\OptiPaie DZ\", StringComparison.OrdinalIgnoreCase) < 0 &&
                        db.IndexOf("/OptiPaie DZ/", StringComparison.OrdinalIgnoreCase) < 0, Is.True, "never the live database");

            XElement settings = XDocument.Load(configPath).Root.Element("appSettings");
            string Setting(string k) => settings.Elements("add").First(e => (string)e.Attribute("key") == k).Attribute("value").Value;
            string baseUrl = Setting("Licensing.BaseUrl").TrimEnd('/');
            string publicKey = (string)typeof(OptiPaie.Desktop.App).Assembly.GetType("OptiPaie.Desktop.Composition.LicensingConfig")
                .GetField("PublicKeyHex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);

            var options = new LicensingOptions
            {
                ProductKey = ModuleKeys.Payroll, ActivateUrl = baseUrl + "/activate", ValidateUrl = baseUrl + "/validate",
                AnonKey = Setting("Licensing.AnonKey"), PublicKeyHex = publicKey, AppVersion = appVersion
            };
            var log = new ListLogger();
            var store = new SqliteLicenseStore(new SqliteConnectionFactory(db));
            var client = new LicensingService(new SupabaseLicenseBackend(options, log), new Ed25519LicenseVerifier(options, log),
                new WmiDeviceIdentity(), store, new DpapiLocalCipher(log), options, log);

            var o = new StringBuilder();
            o.AppendLine("REAL validate — production endpoint, real client pipeline, copy of this PC's database");
            o.AppendLine("endpoint      : POST " + options.ValidateUrl);
            o.AppendLine("productKey    : \"" + options.ProductKey + "\"   appVersion: \"" + appVersion + "\"   deviceId: " + Mask(client.DeviceId));
            StoredLicense before = store.Load();
            o.AppendLine("BEFORE  lastValidation=" + before?.LastValidationUtc + "  graceUntil=" + before?.GraceUntilUtc + "  scope=" + Scope(client.Current));

            DateTime sent = DateTime.UtcNow;
            LicenseResult result = client.SynchronizeAsync(CancellationToken.None).GetAwaiter().GetResult();
            o.AppendLine("sent at       : " + sent.ToString("o"));
            o.AppendLine("RESULT        : " + result.Kind + (string.IsNullOrEmpty(result.Message) ? "" : " — " + result.Message));
            foreach (string l in log.Lines) o.AppendLine("client log    : " + l);

            StoredLicense after = store.Load();
            o.AppendLine("AFTER   lastValidation=" + after?.LastValidationUtc + "  graceUntil=" + after?.GraceUntilUtc + "  scope=" + Scope(client.Current));

            // The signed token that arrived, decoded (sensitive fields masked).
            string token = new DpapiLocalCipher(log).Unprotect(after.SignedToken) ?? after.SignedToken;
            string seg = token.Split('.')[0].Replace('-', '+').Replace('_', '/');
            seg += new string('=', (4 - seg.Length % 4) % 4);
            JObject payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(seg)));
            foreach (string k in new[] { "licenseKey", "email", "deviceId", "companyName", "customerName" })
                if (payload[k] != null) payload[k] = Mask((string)payload[k]);
            o.AppendLine("TOKEN PAYLOAD (signature verified by the client):");
            o.AppendLine(payload.ToString());

            string outDir = Path.Combine(FindRepoRoot(), "docs", "licence-demo");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "real-validate-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmm") + ".txt"), o.ToString(), new UTF8Encoding(true));
            TestContext.Progress.WriteLine(o.ToString());
            Assert.That(result.Kind, Is.EqualTo(LicenseResultKind.Ok));
        }

        private static string Scope(LicenseSnapshot s) =>
            s == null ? "?" : s.State + " / maxCompanies=" + (s.MaxCompanies?.ToString() ?? "null") + (s.IsMultiCompany ? " (MULTI)" : " (MONO)");

        private static string Mask(string v) => string.IsNullOrEmpty(v) || v.Length < 6 ? "•••" : v.Substring(0, 4) + "•••" + v.Substring(v.Length - 2);

        private static string FindRepoRoot()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OptiPaie.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }

        private sealed class ListLogger : OptiPaie.Common.Logging.ILogger
        {
            public readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
            public void Info(string message) => Lines.Add("INFO " + message);
            public void Warn(string message) => Lines.Add("WARN " + message);
            public void Error(string message) => Lines.Add("ERROR " + message);
            public void Error(string message, Exception exception) => Lines.Add("ERROR " + message + " " + exception.Message);
        }
    }
}
