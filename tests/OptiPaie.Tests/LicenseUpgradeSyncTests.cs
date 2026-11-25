using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
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
    /// A licence changed on the server (Mono ⇄ Multi) reaches the client on the next check — the one the
    /// "تحديث الترخيص" button triggers — through the REAL pipeline: <see cref="LicensingService"/>,
    /// <see cref="Ed25519LicenseVerifier"/>, <see cref="SqliteLicenseStore"/> on a temp database and
    /// <see cref="LicenseGate"/>. Only the network is replaced: a fake server signs tokens with a test
    /// Ed25519 key exactly like the backend does. Proves: the upgrade applies and survives a restart,
    /// offline/errors never downgrade, a forged token is refused, and a downgrade keeps every company.
    /// </summary>
    [TestFixture]
    public sealed class LicenseUpgradeSyncTests
    {
        private const string Key = "PAY-TEST-0001";
        private const string Device = "DEV-TEST-1";

        private string _dir;
        private SqliteConnectionFactory _factory;
        private Ed25519PrivateKeyParameters _serverKey;
        private LicensingOptions _options;
        private FakeServer _server;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-licence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _serverKey = NewKey(out string publicHex);
            _options = new LicensingOptions
            {
                ProductKey = "payroll", ActivateUrl = "https://example.invalid/activate", ValidateUrl = "https://example.invalid/validate",
                AnonKey = "anon", PublicKeyHex = publicHex, AppVersion = "1.45.0"
            };
            _server = new FakeServer(this);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private LicensingService NewClient() =>
            new LicensingService(_server, new Ed25519LicenseVerifier(_options, new NullLogger()), new FakeDevice(),
                new SqliteLicenseStore(_factory), new PlainCipher(), _options, new NullLogger());

        private static LicenseGate Gate(LicensingService licensing) => new LicenseGate(licensing, new NoTrial());

        private LicensingService ActivatedAs(int maxCompanies)
        {
            _server.MaxCompanies = maxCompanies;
            LicensingService client = NewClient();
            LicenseResult activated = client.ActivateAsync(Key, "Atlas Groupe", "rh@atlas.dz", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(activated.Kind, Is.EqualTo(LicenseResultKind.Ok), activated.Message);
            return client;
        }

        // ---------------------------------------------------------------- PROOF L1

        [Test]
        public void L1_UpgradeMonoToMulti_ReachesTheClientOnRefresh_AndSurvivesARestart()
        {
            LicensingService client = ActivatedAs(1);
            LicenseGate gate = Gate(client);
            Assert.That(gate.MaxCompanies, Is.EqualTo(1), "starts Mono");
            Assert.That(gate.CanAddCompany(1), Is.False, "Mono: a 2nd company is refused");

            int changed = 0;
            client.Changed += (s, e) => changed++;
            LicenseSnapshot before = client.Current;

            _server.MaxCompanies = 0; // the administrator upgrades the licence on the server
            LicenseResult result = client.SynchronizeAsync(CancellationToken.None).GetAwaiter().GetResult(); // the button
            LicenseSnapshot after = client.Current;

            Assert.That(result.Kind, Is.EqualTo(LicenseResultKind.Ok));
            Assert.That(changed, Is.GreaterThanOrEqualTo(1), "the shell is notified (it announces the change)");
            Assert.That(gate.MaxCompanies, Is.Null, "Multi = unlimited");
            Assert.That(gate.CanAddCompany(1), Is.True);
            Assert.That(gate.CanAddCompany(7), Is.True);

            string message = LicenseScopeChange.Describe(before, after);
            Assert.That(message, Does.StartWith("تم تحديث ترخيصك: أصبح متعدد الشركات."));
            Assert.That(LicenseScopeChange.RefreshReport(result, before, after), Is.EqualTo(message));

            // A restart with NO network still reads Multi: the new signed token was persisted.
            _server.Offline = true;
            LicensingService restarted = NewClient();
            Assert.That(restarted.Current.IsMultiCompany, Is.True);
            Assert.That(Gate(restarted).CanAddCompany(3), Is.True);
            Assert.That(_server.ValidateCalls, Is.EqualTo(1), "exactly one server check");
        }

        // ---------------------------------------------------------------- PROOF L2

        [Test]
        public void L2_OfflineOrServerErrors_NeverChangeTheLicence()
        {
            LicensingService client = ActivatedAs(0); // Multi
            LicenseGate gate = Gate(client);
            string storedBefore = StoredToken();

            foreach (Action<FakeServer> failure in new Action<FakeServer>[]
            {
                s => { s.Offline = true; s.ErrorCode = null; },
                s => { s.Offline = false; s.ErrorCode = "invalid_key"; },
                s => { s.Offline = false; s.ErrorCode = "unknown_product"; },
                s => { s.Offline = false; s.ErrorCode = "server_error"; },
            })
            {
                failure(_server);
                _server.MaxCompanies = 1; // even if the server row now says Mono, a FAILED check must not apply it
                LicenseSnapshot before = client.Current;
                LicenseResult result = client.SynchronizeAsync(CancellationToken.None).GetAwaiter().GetResult();

                Assert.That(result.Kind, Is.Not.EqualTo(LicenseResultKind.Ok));
                Assert.That(gate.MaxCompanies, Is.Null, "still Multi after " + (_server.ErrorCode ?? "offline"));
                Assert.That(client.Current.IsUsable, Is.True, "still usable");
                Assert.That(StoredToken(), Is.EqualTo(storedBefore), "stored token untouched");
                Assert.That(LicenseScopeChange.Describe(before, client.Current), Is.Null, "no scope-change message");
                Assert.That(LicenseScopeChange.RefreshReport(result, before, client.Current),
                    Does.Contain("rien n'a changé").Or.Contain("est conservée"));
            }
        }

        // ---------------------------------------------------------------- PROOF L3

        [Test]
        public void L3_AForgedOrForeignMultiToken_IsRefused()
        {
            LicensingService client = ActivatedAs(1);
            LicenseGate gate = Gate(client);

            _server.MaxCompanies = 0;
            _server.SignWith = NewKey(out _); // not the backend's key
            Assert.That(client.SynchronizeAsync(CancellationToken.None).GetAwaiter().GetResult().Kind, Is.EqualTo(LicenseResultKind.Error));
            Assert.That(gate.MaxCompanies, Is.EqualTo(1), "a forged Multi token is ignored");

            _server.SignWith = null;
            _server.DeviceOverride = "ANOTHER-PC";
            Assert.That(client.SynchronizeAsync(CancellationToken.None).GetAwaiter().GetResult().Kind, Is.EqualTo(LicenseResultKind.Error));
            Assert.That(gate.MaxCompanies, Is.EqualTo(1), "a Multi token for another computer is ignored");
        }

        // ---------------------------------------------------------------- PROOF L4

        [Test]
        public void L4_DowngradeMultiToMono_KeepsEveryCompany_OnlyBlocksCreation()
        {
            LicensingService client = ActivatedAs(0);
            LicenseGate gate = Gate(client);
            using (var c = _factory.CreateOpenConnection())
            {
                foreach (string n in new[] { "Atlas Groupe", "Atlas Logistique", "Atlas Services" })
                    c.Execute("INSERT INTO Companies (NameFr, CreatedAtUtc) VALUES (@n, '2026-01-01');", new { n });
            }
            LicenseSnapshot before = client.Current;

            _server.MaxCompanies = 1;
            LicenseResult result = client.SynchronizeAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Kind, Is.EqualTo(LicenseResultKind.Ok));
            Assert.That(gate.CanAddCompany(3), Is.False, "a 4th company is refused");
            using (var c = _factory.CreateOpenConnection())
                Assert.That(c.ExecuteScalar<long>("SELECT COUNT(*) FROM Companies WHERE IsDeleted = 0"), Is.EqualTo(3), "all 3 stay");
            string message = LicenseScopeChange.Describe(before, client.Current);
            Assert.That(message, Does.Contain("تبقى جميع شركاتك الحالية متاحة"));
            Assert.That(message, Does.Contain("rien n'a été supprimé"));
        }

        // ---------------------------------------------------------------- fakes

        private string StoredToken()
        {
            using (var c = _factory.CreateOpenConnection())
                return c.ExecuteScalar<string>("SELECT SignedToken FROM LicenseState WHERE Id = 1;");
        }

        /// <summary>Stands in for the backend: answers like the deployed functions, signing with a test key.</summary>
        private sealed class FakeServer : ILicenseBackend
        {
            private readonly LicenseUpgradeSyncTests _t;
            public FakeServer(LicenseUpgradeSyncTests t) { _t = t; }

            public int MaxCompanies { get; set; } = 1;
            public bool Offline { get; set; }
            public string ErrorCode { get; set; }
            public Ed25519PrivateKeyParameters SignWith { get; set; }
            public string DeviceOverride { get; set; }
            public int ValidateCalls { get; private set; }

            public Task<BackendLicenseResponse> ActivateAsync(ActivationRequest request, CancellationToken cancellationToken) =>
                Task.FromResult(Respond(request.DeviceId));

            public Task<BackendLicenseResponse> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
            {
                ValidateCalls++;
                if (Offline) throw new HttpRequestException("No such host is known.");
                if (ErrorCode != null) return Task.FromResult(new BackendLicenseResponse { Success = false, ErrorCode = ErrorCode });
                return Task.FromResult(Respond(request.DeviceId));
            }

            public Task<BackendLicenseResponse> ActivateModuleAsync(ModuleActivationRequest request, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            private BackendLicenseResponse Respond(string deviceId)
            {
                string token = Sign(new
                {
                    v = 1, product = "payroll", licenseKey = Key, companyName = "Atlas Groupe", email = "rh@atlas.dz",
                    deviceId = DeviceOverride ?? deviceId, status = "active", type = "annual", maxCompanies = MaxCompanies,
                    modules = new[] { "payroll" }, issuedAt = DateTime.UtcNow, expiresAt = DateTime.UtcNow.AddYears(1),
                    graceUntil = DateTime.UtcNow.AddDays(30)
                }, SignWith ?? _t._serverKey);
                return new BackendLicenseResponse { Success = true, Token = token, Status = "active", Modules = new[] { "payroll" } };
            }
        }

        private sealed class FakeDevice : IDeviceIdentity
        {
            public string GetDeviceId() => Device;
            public string GetDeviceInfo() => "test";
        }

        private sealed class PlainCipher : ILocalCipher
        {
            public string Protect(string plainText) => plainText;
            public string Unprotect(string protectedBase64) => protectedBase64;
        }

        private sealed class NoTrial : ITrialService
        {
            public TrialInfo GetStatus() => TrialInfo.NotStarted();
            public TrialInfo StartTrial() => TrialInfo.NotStarted();
        }

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
            byte[] pub = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
            var sb = new StringBuilder();
            foreach (byte b in pub) sb.Append(b.ToString("x2"));
            publicHex = sb.ToString();
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
