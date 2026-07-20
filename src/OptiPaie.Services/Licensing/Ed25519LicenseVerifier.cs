using System;
using System.Text;
using Newtonsoft.Json;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// Verifies license tokens of the form <c>base64url(payloadJson).base64url(signature)</c>
    /// using an embedded Ed25519 public key (BouncyCastle — .NET Framework 4.8 has no
    /// built-in Ed25519). The signature is checked over the ASCII bytes of the left
    /// segment, exactly as the backend signs it, so there is no JSON-canonicalisation
    /// ambiguity. The payload is only deserialised AFTER the signature is confirmed.
    /// </summary>
    public sealed class Ed25519LicenseVerifier : ISignedLicenseVerifier
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.DateTime,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly byte[] _publicKey;
        private readonly ILogger _logger;

        public Ed25519LicenseVerifier(LicensingOptions options, ILogger logger)
        {
            Guard.AgainstNull(options, nameof(options));
            _logger = Guard.AgainstNull(logger, nameof(logger));
            _publicKey = TryDecodeHex(options.PublicKeyHex);
        }

        public bool TryVerify(string token, out SignedLicensePayload payload)
        {
            payload = null;

            if (_publicKey == null || _publicKey.Length != 32 || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            int dot = token.IndexOf('.');
            if (dot <= 0 || dot >= token.Length - 1)
            {
                return false;
            }

            string encodedPayload = token.Substring(0, dot);
            string encodedSignature = token.Substring(dot + 1);

            byte[] payloadBytes;
            byte[] signature;
            try
            {
                payloadBytes = Base64UrlDecode(encodedPayload);
                signature = Base64UrlDecode(encodedSignature);
            }
            catch
            {
                return false;
            }

            try
            {
                byte[] message = Encoding.ASCII.GetBytes(encodedPayload);
                var publicKey = new Ed25519PublicKeyParameters(_publicKey);
                var verifier = new Ed25519Signer();
                verifier.Init(false, publicKey);
                verifier.BlockUpdate(message, 0, message.Length);
                if (!verifier.VerifySignature(signature))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("License signature verification error: " + ex.Message);
                return false;
            }

            try
            {
                string json = Encoding.UTF8.GetString(payloadBytes);
                payload = JsonConvert.DeserializeObject<SignedLicensePayload>(json, JsonSettings);
                return payload != null;
            }
            catch (Exception ex)
            {
                _logger.Warn("License payload parse error: " + ex.Message);
                payload = null;
                return false;
            }
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }

            return Convert.FromBase64String(s);
        }

        private static byte[] TryDecodeHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length % 2 != 0)
            {
                return null;
            }

            try
            {
                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                }

                return bytes;
            }
            catch
            {
                return null;
            }
        }
    }
}
