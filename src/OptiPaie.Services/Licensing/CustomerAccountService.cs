using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// <see cref="ICustomerAccountService"/> backed by the local settings store. The
    /// account is a single record kept in the key/value <see cref="ISettingsService"/>
    /// (no schema migration needed): email + company in the clear (also on the license),
    /// the password only as a DPAPI-wrapped PBKDF2 hash, and a "signed-in" flag.
    /// <para>
    /// Everything here is local and synchronous, so login/logout after the first
    /// activation works with no network at all.
    /// </para>
    /// </summary>
    public sealed class CustomerAccountService : ICustomerAccountService
    {
        private const string KeyEmail = "Account.Email";
        private const string KeyCompany = "Account.Company";
        private const string KeySecret = "Account.Secret";      // DPAPI( pbkdf2 blob )
        private const string KeySignedIn = "Account.SignedIn";  // "1" / "0"
        private const string KeyCreated = "Account.CreatedUtc";
        private const string KeyRecovery = "Account.Recovery";          // DPAPI( pbkdf2 of the one-time code )
        private const string KeyRecFails = "Account.RecoveryFails";     // consecutive failed attempts
        private const string KeyRecLock = "Account.RecoveryLockUntil";  // ISO-8601 UTC lockout expiry

        private const int SaltBytes = 16;
        private const int HashBytes = 32;
        private const int Iterations = 100_000;

        private const int MinPasswordLength = 10;
        private const int MaxRecoveryFails = 5;
        private const int RecoveryLockMinutes = 15;
        // Unambiguous Crockford-style alphabet (no 0/O/1/I) for a human-copyable code.
        private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

        private readonly ISettingsService _settings;
        private readonly ILocalCipher _cipher;
        private readonly ILogger _logger;

        public CustomerAccountService(ISettingsService settings, ILocalCipher cipher, ILogger logger)
        {
            _settings = Guard.AgainstNull(settings, nameof(settings));
            _cipher = Guard.AgainstNull(cipher, nameof(cipher));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        /// <summary>Audit sink for recovery attempts (best-effort; wired by composition).</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        public bool HasAccount =>
            !string.IsNullOrWhiteSpace(Get(KeyEmail)) && !string.IsNullOrWhiteSpace(Get(KeySecret));

        public bool IsSignedIn => HasAccount && Get(KeySignedIn) == "1";

        public string Email => Get(KeyEmail) ?? string.Empty;

        public string Company => Get(KeyCompany) ?? string.Empty;

        public void Register(string email, string company, string password)
        {
            string normalizedEmail = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Email and password are required to register an account.");
            }

            string blob = HashPassword(password);
            string wrapped = _cipher.Protect(blob) ?? blob; // DPAPI-wrap; fall back keeps it working

            Set(KeyEmail, normalizedEmail);
            Set(KeyCompany, (company ?? string.Empty).Trim());
            Set(KeySecret, wrapped);
            Set(KeyCreated, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Set(KeySignedIn, "1");

            _logger.Info("Customer account registered and signed in.");
        }

        public bool SignIn(string email, string password)
        {
            if (!HasAccount || password == null)
            {
                return false;
            }

            // Single local account: the email must match the one on file.
            if (!string.Equals((email ?? string.Empty).Trim(), Email, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string wrapped = Get(KeySecret);
            string blob = _cipher.Unprotect(wrapped) ?? wrapped; // unwrap; tolerate legacy plaintext
            if (!VerifyPassword(password, blob))
            {
                return false;
            }

            Set(KeySignedIn, "1");
            _logger.Info("Customer account signed in.");
            return true;
        }

        public void SignOut()
        {
            if (HasAccount)
            {
                Set(KeySignedIn, "0");
                _logger.Info("Customer account signed out (account and license kept).");
            }
        }

        public void Clear()
        {
            Set(KeyEmail, string.Empty);
            Set(KeyCompany, string.Empty);
            Set(KeySecret, string.Empty);
            Set(KeySignedIn, "0");
            Set(KeyCreated, string.Empty);
            Set(KeyRecovery, string.Empty);
            Set(KeyRecFails, string.Empty);
            Set(KeyRecLock, string.Empty);
        }

        // -- owner password recovery (offline, one-time code) -----------------

        public bool HasRecoveryCode => !string.IsNullOrWhiteSpace(Get(KeyRecovery));

        public string IssueRecoveryCode()
        {
            if (!HasAccount)
            {
                return null;
            }

            string code = GenerateCode();
            string blob = HashPassword(Normalize(code));          // hash the code exactly like a password
            Set(KeyRecovery, _cipher.Protect(blob) ?? blob);       // DPAPI-wrapped; store only the hash
            Set(KeyRecFails, "0");
            Set(KeyRecLock, string.Empty);
            _logger.Info("Owner recovery code issued.");
            return code;
        }

        public RecoveryResult RecoverWithCode(string code, string newPassword)
        {
            if (!HasAccount || !HasRecoveryCode)
            {
                return RecoveryResult.Fail(RecoveryFailure.NoAccount);
            }

            // Rate limit: a lockout window after too many recent failures (fully offline clock).
            DateTime now = DateTime.UtcNow;
            if (TryGetLockUntil(out DateTime until) && now < until)
            {
                Audit.Record("AccountRecovery", 0, AuditAction.StatusChanged,
                    "Tentative de récupération du compte propriétaire refusée (verrouillage temporaire).");
                return RecoveryResult.Fail(RecoveryFailure.Locked);
            }

            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
            {
                return RecoveryResult.Fail(RecoveryFailure.WeakPassword);
            }

            string wrapped = Get(KeyRecovery);
            string recBlob = _cipher.Unprotect(wrapped) ?? wrapped;
            bool ok = !string.IsNullOrEmpty(code) && VerifyPassword(Normalize(code), recBlob);

            if (!ok)
            {
                int fails = ReadInt(KeyRecFails) + 1;
                Set(KeyRecFails, fails.ToString(CultureInfo.InvariantCulture));
                if (fails >= MaxRecoveryFails)
                {
                    Set(KeyRecLock, now.AddMinutes(RecoveryLockMinutes).ToString("o", CultureInfo.InvariantCulture));
                    Set(KeyRecFails, "0");
                }
                Audit.Record("AccountRecovery", 0, AuditAction.StatusChanged,
                    "Échec d'une tentative de récupération du compte propriétaire (code incorrect).");
                return RecoveryResult.Fail(RecoveryFailure.BadCode);
            }

            // Success: set the new password, CONSUME the used code and issue a fresh one.
            string pwdBlob = HashPassword(newPassword);
            Set(KeySecret, _cipher.Protect(pwdBlob) ?? pwdBlob);
            Set(KeyRecFails, "0");
            Set(KeyRecLock, string.Empty);
            string newCode = IssueRecoveryCode();      // replaces the consumed code
            Set(KeySignedIn, "1");                      // the owner is now in
            Audit.Record("AccountRecovery", 0, AuditAction.StatusChanged,
                "Mot de passe du compte propriétaire réinitialisé via le code de récupération.");
            _logger.Info("Owner password recovered via one-time code.");
            return RecoveryResult.Ok(newCode);
        }

        private bool TryGetLockUntil(out DateTime until)
        {
            until = DateTime.MinValue;
            string raw = Get(KeyRecLock);
            return !string.IsNullOrWhiteSpace(raw) &&
                DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out until);
        }

        private int ReadInt(string key)
        {
            return int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        }

        /// <summary>A 20-character code in 4 groups of 5 (e.g. "H8K2M-NPQR3-...") from a crypto RNG.</summary>
        private static string GenerateCode()
        {
            var sb = new StringBuilder();
            byte[] bytes = new byte[20];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            for (int i = 0; i < 20; i++)
            {
                if (i > 0 && i % 5 == 0) sb.Append('-');
                sb.Append(CodeAlphabet[bytes[i] % CodeAlphabet.Length]);
            }
            return sb.ToString();
        }

        /// <summary>Canonicalise a typed code: upper-case, strip spaces/dashes, so formatting never matters.</summary>
        private static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            var sb = new StringBuilder(code.Length);
            foreach (char c in code)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        // -- password hashing (PBKDF2-SHA256) ---------------------------------

        private static string HashPassword(string password)
        {
            byte[] salt = new byte[SaltBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            byte[] hash = Derive(password, salt, Iterations);
            return string.Join("$",
                "v1",
                Iterations.ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash));
        }

        private static bool VerifyPassword(string password, string blob)
        {
            if (string.IsNullOrEmpty(blob))
            {
                return false;
            }

            string[] parts = blob.Split('$');
            if (parts.Length != 4 || parts[0] != "v1")
            {
                return false;
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations))
            {
                return false;
            }

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch
            {
                return false;
            }

            byte[] actual = Derive(password, salt, iterations);
            return FixedTimeEquals(actual, expected);
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashBytes);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }

        // -- settings helpers -------------------------------------------------

        private string Get(string key)
        {
            try { return _settings.Get(key, null); }
            catch (Exception ex) { _logger.Warn("Could not read account setting '" + key + "': " + ex.Message); return null; }
        }

        private void Set(string key, string value)
        {
            try { _settings.Set(key, value ?? string.Empty); }
            catch (Exception ex) { _logger.Warn("Could not write account setting '" + key + "': " + ex.Message); }
        }
    }
}
