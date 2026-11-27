using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using OptiPaie.Core.Licensing;
using OptiPaie.Data.Context;
using OptiPaie.Data.Licensing;
using OptiPaie.Data.Migrations;
using OptiPaie.Services.Licensing;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace OptiPaie.Tests
{
    /// <summary>
    /// "LIFETIME MEANS LIFETIME." A lifetime licence (no end date), once activated, never locks, never
    /// expires and never signs anyone out, however long the poste stays offline, whatever the server
    /// answers. Licences with an END DATE and TRIALS keep their limits. Real pipeline: LicensingService,
    /// Ed25519LicenseVerifier, SqliteLicenseStore on a temp database, LicenseGate / AccessGate; tokens
    /// signed like the backend; the "garbage" cases go through the real SupabaseLicenseBackend HTTP client.
    /// Time passing offline = the client's own anti-rollback clock floor (LastSeenUtc) moved forward.
    /// </summary>
    [TestFixture]
    public sealed class LifetimeNeverLocksTests
    {
        private const string Key = "PAY-LIFE-0001-TEST";
        private string _dir;
        private SqliteConnectionFactory _factory;
        private SqliteLicenseStore _store;
        private Ed25519PrivateKeyParameters _serverKey;
        private string _publicHex;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-lifetime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _store = new SqliteLicenseStore(_factory);
            _serverKey = NewKey(out _publicHex);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ------------------------------------------------------------------ lifetime, offline

        [TestCase(31)]
        [TestCase(400)]
        [TestCase(3653)] // ten years
        public void Lifetime_TokenFromTheOldServer_StillCarryingAGraceDate_NeverLocks(int daysOffline)
        {
            var server = new Server(this) { GraceDays = 30 };            // exactly what customers hold today
            LicensingService client = Activate(server);
            Offline(daysOffline);
            AssertFullAccess(client, daysOffline + " days offline, old-server token");
        }

        [TestCase(31)]
        [TestCase(400)]
        [TestCase(3653)]
        public void Lifetime_TokenFromTheFixedServer_NoGraceDate_NeverLocks(int daysOffline)
        {
            var server = new Server(this) { GraceDays = null };          // the fixed server: no deadline
            LicensingService client = Activate(server);
            Offline(daysOffline);
            AssertFullAccess(client, daysOffline + " days offline, fixed-server token");
        }

        // ------------------------------------------------------------------ the server misbehaves

        [Test]
        public void Lifetime_ServerUnreachable_ErrorOrRefusal_CustomerKeepsFullAccess_Every_Time()
        {
            var server = new Server(this) { GraceDays = 30, MaxCompanies = 0 };
            LicensingService client = Activate(server);
            var gate = new LicenseGate(client, new NoTrial());
            Offline(400);

            foreach (Action<Server> failure in new Action<Server>[]
            {
                s => { s.Throw = new HttpRequestException("No such host is known."); },
                s => { s.Throw = new TaskCanceledException("timeout"); },
                s => { s.Throw = null; s.ErrorCode = "server_error"; },
                s => { s.ErrorCode = "invalid_key"; },
                s => { s.ErrorCode = "device_mismatch"; },
                s => { s.ErrorCode = "unknown_product"; },
                s => { s.ErrorCode = null; s.Garbage = "this is not a token"; },
                s => { s.Garbage = "eyJ2IjoxfQ.c2lnbmF0dXJl"; },   // well-formed, wrong signature
            })
            {
                failure(server);
                LicenseResult r = client.SynchronizeAsync(CancellationToken.None).GetAwaiter().GetResult();
                AssertFullAccess(client, "after " + r.Kind + " " + (server.ErrorCode ?? server.Garbage ?? server.Throw?.GetType().Name));
                Assert.That(gate.MaxCompanies, Is.Null, "a Multi licence stays Multi through any failure");
            }
        }

        [TestCase("HTTP/1.1 200 OK", "<html>garbage</html>")]
        [TestCase("HTTP/1.1 200 OK", "{\"token\":12,\"status\":[]}")]
        [TestCase("HTTP/1.1 500 Internal Server Error", "{\"error\":\"server_error\"}")]
        [TestCase("HTTP/1.1 502 Bad Gateway", "")]
        public void Lifetime_RealHttpClient_ServerReturnsGarbageOrError_CustomerKeepsFullAccess(string statusLine, string body)
        {
            LicensingService activated = Activate(new Server(this) { GraceDays = 30 });
            Offline(400);
            using (var junk = new JunkHttpServer(statusLine, body))
            {
                LicensingOptions options = Options();
                options.ValidateUrl = junk.Url;
                var client = new LicensingService(new SupabaseLicenseBackend(options, new NullLogger()), Verifier(), new FakeDevice(),
                    _store, new PlainCipher(), options, new NullLogger());
                client.SynchronizeAsync(CancellationToken.None).GetAwaiter().GetResult();
                AssertFullAccess(client, statusLine + " " + body);
            }
        }

        // ------------------------------------------------------------------ what must NOT change

        [Test]
        public void ALicenceWithAnEndDate_StillStopsAtItsEndDate()
        {
            var server = new Server(this) { GraceDays = 30, ExpiresInDays = 10, Type = "annual" };
            LicensingService client = Activate(server);
            Assert.That(client.Current.IsUsable, Is.True, "usable before its end date");
            Offline(11);
            Assert.That(client.Refresh().State, Is.EqualTo(LicenseStateKind.Expired), "stops at its end date");
            Assert.That(client.Current.IsUsable, Is.False);
        }

        [Test]
        public void ALicenceWithAnEndDate_KeepsItsOfflineGraceExactlyAsBefore()
        {
            var server = new Server(this) { GraceDays = 30, ExpiresInDays = 365, Type = "annual" };
            LicensingService client = Activate(server);
            Offline(29);
            Assert.That(client.Refresh().IsUsable, Is.True, "inside its 30-day grace");
            Offline(31);
            Assert.That(client.Refresh().State, Is.EqualTo(LicenseStateKind.GraceExpired), "unchanged: an end-dated licence still needs to check in");
        }

        [Test]
        public void ATrial_StillExpiresOnSchedule()
        {
            var store = new TrialStoreStub();
            var trial = new TrialService(store, new PlainCipher(), new NullLogger());
            Assert.That(trial.StartTrial().IsActive, Is.True);
            store.ExpireNow();
            TrialInfo after = new TrialService(store, new PlainCipher(), new NullLogger()).GetStatus();
            Assert.That(after.IsExpired, Is.True, "the trial path is untouched");

            var noLicence = new LicensingService(new Server(this), Verifier(), new FakeDevice(), _store, new PlainCipher(), Options(), new NullLogger());
            Assert.That(new AccessGate(noLicence, new TrialService(store, new PlainCipher(), new NullLogger())).Evaluate().State,
                Is.EqualTo(AccessState.TrialExpired));
        }

        [Test]
        public void NoCodePath_SignsTheUserOut_BecauseOfALicenceState()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "OptiPaie.sln"))) root = root.Parent;
            Assert.That(root, Is.Not.Null);
            string src = Path.Combine(root.FullName, "src");

            // Every place that ends a session or the owner sign-in, and the only reasons allowed.
            var allowed = new Dictionary<string, string>
            {
                [@"OptiPaie.Services\Licensing\CustomerAccountService.cs"] = "the SignOut() definition itself",
                [@"OptiPaie.Core\Licensing\ICustomerAccountService.cs"] = "the interface",
                [@"OptiPaie.Desktop\App.xaml.cs"] = "App.SignOut(), reached only from the user's own Se déconnecter button",
                [@"OptiPaie.Desktop\Shell\ShellViewModel.cs"] = "the Se déconnecter button command",
                [@"OptiPaie.Admin\Api\SupabaseAdminClient.cs"] = "the separate Admin tool",
            };
            var offenders = new List<string>();
            foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;
                string rel = file.Substring(src.Length + 1);
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i].Trim();
                    if (l.StartsWith("//") || l.StartsWith("///")) continue;
                    if (Regex.IsMatch(l, @"\bSignOut\s*\(") || Regex.IsMatch(l, @"Session\.Current\s*=\s*null"))
                        if (!allowed.ContainsKey(rel)) offenders.Add(rel + ":" + (i + 1) + "  " + l);
                }
            }
            Assert.That(offenders, Is.Empty, "a sign-out outside the user's own button");

            // App.SignOut() must be reached only by the button, never by a licence/access state.
            string app = File.ReadAllText(Path.Combine(src, @"OptiPaie.Desktop\App.xaml.cs"));
            string shell = File.ReadAllText(Path.Combine(src, @"OptiPaie.Desktop\Shell\ShellViewModel.cs"));
            Assert.That(Regex.Matches(shell, @"\.SignOut\(\)").Count, Is.EqualTo(1), "only SignOutCommand");
            // A bare "SignOut();" (not "x.SignOut();", not the "void SignOut()" declaration) would be App signing itself out.
            Assert.That(Regex.IsMatch(app, @"(?<![.\w])SignOut\(\)\s*;"), Is.False, "App never calls its own SignOut()");
            foreach (string licensingFile in Directory.EnumerateFiles(Path.Combine(src, @"OptiPaie.Services\Licensing"), "*.cs")
                         .Where(f => !f.EndsWith("CustomerAccountService.cs")))
                Assert.That(File.ReadAllText(licensingFile), Does.Not.Contain("SignOut"), Path.GetFileName(licensingFile));
        }

        // ------------------------------------------------------------------ helpers

        private void AssertFullAccess(LicensingService client, string why)
        {
            LicenseSnapshot s = client.Refresh();
            Assert.That(s.State, Is.EqualTo(LicenseStateKind.Active), why);
            Assert.That(s.IsUsable, Is.True, why);
            var gate = new LicenseGate(client, new NoTrial());
            Assert.That(gate.IsUsable, Is.True, why);
            Assert.That(gate.IsEnabled(ModuleKeys.Payroll), Is.True, why);
            Assert.That(new AccessGate(client, new NoTrial()).Evaluate().State, Is.EqualTo(AccessState.Licensed), why);
        }

        /// <summary>Moves the client's clock floor forward: "it is now N days later and nothing reached the server".</summary>
        private void Offline(int days) => _store.UpdateLastSeen(DateTime.UtcNow.AddDays(days).ToString("o"));

        private LicensingService Activate(Server server)
        {
            var client = new LicensingService(server, Verifier(), new FakeDevice(), _store, new PlainCipher(), Options(), new NullLogger());
            LicenseResult r = client.ActivateAsync(Key, "SARL Test", "rh@test.dz", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(r.Kind, Is.EqualTo(LicenseResultKind.Ok), r.Message);
            return client;
        }

        private LicensingOptions Options() => new LicensingOptions
        {
            ProductKey = "payroll", ActivateUrl = "https://example.invalid/activate", ValidateUrl = "https://example.invalid/validate",
            AnonKey = "anon", PublicKeyHex = _publicHex, AppVersion = "1.45.2"
        };

        private Ed25519LicenseVerifier Verifier() => new Ed25519LicenseVerifier(Options(), new NullLogger());

        /// <summary>The backend: signs tokens like the real one; can be told to fail in every way.</summary>
        private sealed class Server : ILicenseBackend
        {
            private readonly LifetimeNeverLocksTests _t;
            public Server(LifetimeNeverLocksTests t) { _t = t; }
            public int? GraceDays { get; set; } = 30;
            public int? ExpiresInDays { get; set; }
            public string Type { get; set; } = "lifetime";
            public int MaxCompanies { get; set; } = 1;
            public Exception Throw { get; set; }
            public string ErrorCode { get; set; }
            public string Garbage { get; set; }

            public Task<BackendLicenseResponse> ActivateAsync(ActivationRequest request, CancellationToken ct) => Task.FromResult(Token(request.DeviceId));

            public Task<BackendLicenseResponse> ValidateAsync(ValidationRequest request, CancellationToken ct)
            {
                if (Throw != null) throw Throw;
                if (ErrorCode != null) return Task.FromResult(new BackendLicenseResponse { Success = false, ErrorCode = ErrorCode });
                if (Garbage != null) return Task.FromResult(new BackendLicenseResponse { Success = true, Token = Garbage, Status = "active" });
                return Task.FromResult(Token(request.DeviceId));
            }

            public Task<BackendLicenseResponse> ActivateModuleAsync(ModuleActivationRequest request, CancellationToken ct) => throw new NotSupportedException();

            private BackendLicenseResponse Token(string deviceId)
            {
                DateTime now = DateTime.UtcNow;
                var payload = new Dictionary<string, object>
                {
                    ["v"] = 1, ["product"] = "payroll", ["licenseKey"] = Key, ["companyName"] = "SARL Test", ["email"] = "rh@test.dz",
                    ["deviceId"] = deviceId, ["status"] = "active", ["type"] = Type, ["maxCompanies"] = MaxCompanies,
                    ["modules"] = new[] { "payroll" }, ["issuedAt"] = now,
                    ["expiresAt"] = ExpiresInDays.HasValue ? (object)now.AddDays(ExpiresInDays.Value) : null,
                    ["graceUntil"] = GraceDays.HasValue ? (object)now.AddDays(GraceDays.Value) : null,
                };
                return new BackendLicenseResponse { Success = true, Token = Sign(payload, _t._serverKey), Status = "active", Modules = new[] { "payroll" } };
            }
        }

        /// <summary>A real TCP server that answers every request with a fixed (junk) HTTP response.</summary>
        private sealed class JunkHttpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            public string Url { get; }

            public JunkHttpServer(string statusLine, string body)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/validate";
                byte[] bytes = Encoding.ASCII.GetBytes(statusLine + "\r\nContent-Type: text/html\r\nContent-Length: " +
                                                       Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body);
                Task.Run(async () =>
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        TcpClient c;
                        try { c = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); } catch { return; }
                        using (c)
                        using (NetworkStream s = c.GetStream())
                        {
                            var buf = new byte[8192];
                            try { await s.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false); await s.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false); } catch { }
                        }
                    }
                });
            }

            public void Dispose() { _stop.Cancel(); _listener.Stop(); }
        }

        private sealed class TrialStoreStub : ITrialStore
        {
            private string _blob;
            public string Load() => _blob;
            public void Save(string blob) => _blob = blob;
            public void Clear() => _blob = null;
            public void ExpireNow()
            {
                string past = DateTime.UtcNow.AddHours(-1).ToString("o");
                _blob = "{\"StartedUtc\":\"" + DateTime.UtcNow.AddHours(-49).ToString("o") + "\",\"ExpiresUtc\":\"" + past + "\",\"LastSeenUtc\":\"" + past + "\"}";
            }
        }

        private sealed class FakeDevice : IDeviceIdentity { public string GetDeviceId() => "DEV-LIFE-1"; public string GetDeviceInfo() => "test"; }
        private sealed class PlainCipher : ILocalCipher { public string Protect(string p) => p; public string Unprotect(string p) => p; }
        private sealed class NoTrial : ITrialService { public TrialInfo GetStatus() => TrialInfo.NotStarted(); public TrialInfo StartTrial() => TrialInfo.NotStarted(); }
        private sealed class NullLogger : OptiPaie.Common.Logging.ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }

        private static Ed25519PrivateKeyParameters NewKey(out string publicHex)
        {
            var gen = new Ed25519KeyPairGenerator();
            gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            AsymmetricCipherKeyPair pair = gen.GenerateKeyPair();
            publicHex = string.Concat(((Ed25519PublicKeyParameters)pair.Public).GetEncoded().Select(b => b.ToString("x2")));
            return (Ed25519PrivateKeyParameters)pair.Private;
        }

        private static string Sign(object payload, Ed25519PrivateKeyParameters key)
        {
            string body = B64(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
            byte[] message = Encoding.ASCII.GetBytes(body);
            var signer = new Ed25519Signer();
            signer.Init(true, key);
            signer.BlockUpdate(message, 0, message.Length);
            return body + "." + B64(signer.GenerateSignature());
        }

        private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
