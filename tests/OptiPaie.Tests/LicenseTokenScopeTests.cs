using System;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using OptiPaie.Core.Licensing;
using OptiPaie.Services.Licensing;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Proves the new company-scope field survives the EXACT license-token pipeline the
    /// product uses in production: backend-style Ed25519 signing
    /// (<c>base64url(payloadJson).base64url(signature)</c>, signed over the ASCII bytes of
    /// the left segment) → the desktop's <see cref="Ed25519LicenseVerifier"/> →
    /// <see cref="SignedLicensePayload.MaxCompanies"/>.
    /// <para>
    /// No network or deployed backend is needed: a fresh test keypair stands in for the
    /// server's signing key, and the verifier is handed its public half — so this is a
    /// genuine end-to-end proof of the wire format + parsing, not a mock.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class LicenseTokenScopeTests
    {
        [Test]
        public void SignedToken_MonoScope_VerifiesAndParsesAsOne()
        {
            KeyPair kp = NewKeyPair();
            string token = Sign(new
            {
                v = 1, product = "payroll", licenseKey = "PAY-MONO-0001", companyName = "Atlas Industrie",
                deviceId = "DEV-1", status = "active", type = "lifetime", maxCompanies = 1,
                modules = new[] { "payroll" }
            }, kp.Private);

            var verifier = new Ed25519LicenseVerifier(new LicensingOptions { PublicKeyHex = kp.PublicHex }, new NullLogger());
            bool ok = verifier.TryVerify(token, out SignedLicensePayload payload);

            Assert.That(ok, Is.True, "a correctly signed token must verify");
            Assert.That(payload.MaxCompanies, Is.EqualTo(1), "Mono scope survives sign → verify → parse");
            Assert.That(payload.Product, Is.EqualTo("payroll"));
        }

        [Test]
        public void SignedToken_MultiScope_ParsesAsZero()
        {
            KeyPair kp = NewKeyPair();
            // maxCompanies:0 → Multi-sociétés (unlimited). Multi is ALWAYS an explicit 0.
            string token = Sign(new
            {
                v = 1, product = "payroll", licenseKey = "PAY-MULTI-0001", companyName = "Atlas Groupe",
                deviceId = "DEV-1", status = "active", type = "annual", maxCompanies = 0,
                modules = new[] { "payroll" }
            }, kp.Private);

            var verifier = new Ed25519LicenseVerifier(new LicensingOptions { PublicKeyHex = kp.PublicHex }, new NullLogger());

            Assert.That(verifier.TryVerify(token, out SignedLicensePayload payload), Is.True);
            Assert.That(payload.MaxCompanies, Is.EqualTo(0), "Multi scope is carried as the explicit sentinel 0");
        }

        [Test]
        public void SignedToken_WithoutScopeField_ParsesAsNull()
        {
            KeyPair kp = NewKeyPair();
            // A token that predates the feature carries NO maxCompanies key at all.
            string token = Sign(new
            {
                v = 1, product = "payroll", licenseKey = "PAY-LEGACY-0001", companyName = "Old Co",
                deviceId = "DEV-1", status = "active", type = "lifetime", modules = new[] { "payroll" }
            }, kp.Private);

            var verifier = new Ed25519LicenseVerifier(new LicensingOptions { PublicKeyHex = kp.PublicHex }, new NullLogger());

            Assert.That(verifier.TryVerify(token, out SignedLicensePayload payload), Is.True);
            Assert.That(payload.MaxCompanies, Is.Null,
                "an absent field parses to null — which the gate then reads as Mono (see gate tests)");
        }

        [Test]
        public void TamperingWithScope_InvalidatesTheToken()
        {
            KeyPair kp = NewKeyPair();
            string token = Sign(new
            {
                v = 1, product = "payroll", licenseKey = "PAY-MONO-0002", deviceId = "DEV-1",
                status = "active", type = "lifetime", maxCompanies = 1, modules = new[] { "payroll" }
            }, kp.Private);

            // Forge a Multi payload (scope stripped) but keep the original signature.
            string forgedPayload = Base64Url(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {
                v = 1, product = "payroll", licenseKey = "PAY-MONO-0002", deviceId = "DEV-1",
                status = "active", type = "lifetime", modules = new[] { "payroll" }
            })));
            string forged = forgedPayload + "." + token.Substring(token.IndexOf('.') + 1);

            var verifier = new Ed25519LicenseVerifier(new LicensingOptions { PublicKeyHex = kp.PublicHex }, new NullLogger());
            Assert.That(verifier.TryVerify(forged, out _), Is.False,
                "stripping the scope from a signed token must break verification");
        }

        // ---- helpers mirroring backend _shared/ed25519.ts exactly -------------------

        private struct KeyPair { public string PublicHex; public Ed25519PrivateKeyParameters Private; }

        private static KeyPair NewKeyPair()
        {
            var gen = new Ed25519KeyPairGenerator();
            gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            AsymmetricCipherKeyPair pair = gen.GenerateKeyPair();
            var pub = (Ed25519PublicKeyParameters)pair.Public;
            return new KeyPair { PublicHex = ToHex(pub.GetEncoded()), Private = (Ed25519PrivateKeyParameters)pair.Private };
        }

        private static string Sign(object payload, Ed25519PrivateKeyParameters priv)
        {
            string json = JsonConvert.SerializeObject(payload);
            string b64Payload = Base64Url(Encoding.UTF8.GetBytes(json));
            byte[] message = Encoding.ASCII.GetBytes(b64Payload);
            var signer = new Ed25519Signer();
            signer.Init(true, priv);
            signer.BlockUpdate(message, 0, message.Length);
            return b64Payload + "." + Base64Url(signer.GenerateSignature());
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private sealed class NullLogger : OptiPaie.Common.Logging.ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }
}
