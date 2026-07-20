using System;
using System.Security.Cryptography;
using System.Text;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// <see cref="ILocalCipher"/> backed by Windows DPAPI (CurrentUser scope). The
    /// encrypted blob is bound to the current Windows user and machine, so a copied
    /// cache cannot be decrypted elsewhere. Never throws — returns null on failure so
    /// the caller treats a foreign/corrupt cache as absent.
    /// </summary>
    public sealed class DpapiLocalCipher : ILocalCipher
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OptiPaie.PRO.LicenseCache.v1");

        private readonly ILogger _logger;

        public DpapiLocalCipher(ILogger logger)
        {
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        public string Protect(string plainText)
        {
            if (plainText == null)
            {
                return null;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                _logger.Warn("Local cache encryption failed: " + ex.Message);
                return null;
            }
        }

        public string Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64))
            {
                return null;
            }

            try
            {
                byte[] encrypted = Convert.FromBase64String(protectedBase64);
                byte[] data = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                // Not a DPAPI blob (e.g. legacy plaintext) or foreign/corrupt — caller falls back.
                return null;
            }
        }
    }
}
