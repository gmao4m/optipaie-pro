using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;

namespace OptiPaie.Services
{
    /// <summary>
    /// Local user accounts, roles and the optional login gate. Passwords are hashed with
    /// PBKDF2 (SHA-256) and a per-user salt — the clear password is never stored. The iteration
    /// count is stored WITH each hash ("iterations$hash") so an older hash is recognised and
    /// transparently upgraded to the current cost on the next successful login. The gate stays off
    /// until explicitly enabled, so existing/demo installs run open.
    /// </summary>
    public sealed class UserService : IUserService
    {
        private const string AuthEnabledKey = "Auth.Enabled";

        // Current PBKDF2 cost. Raised from 100k -> 600k (OWASP guidance for PBKDF2-SHA256). Hashes
        // written before this carry their own (lower) count and are re-hashed on next login.
        private const int CurrentIterations = 600000;
        private const int LegacyIterations = 100000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        /// <summary>Minimum password length (raised from 4 to 10).</summary>
        public const int MinPasswordLength = 10;

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly ISettingsService _settings;
        private readonly IAccessContext _access;

        public UserService(IUnitOfWorkFactory unitOfWorkFactory, ISettingsService settings, IAccessContext access = null)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _settings = Guard.AgainstNull(settings, nameof(settings));
            _access = access;
        }

        /// <summary>Audit trail for user management + the login gate (security-relevant). Best-effort.</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        private static string PasswordTooShortMessage =>
            "Le mot de passe doit contenir au moins " + MinPasswordLength + " caractères.\n" +
            "يجب أن تتكوّن كلمة السرّ من " + MinPasswordLength + " محارف على الأقلّ.";

        public Result<long> Create(string username, string fullName, string password, UserRole role, string department, bool mustChangePassword = false)
        {
            AccessPolicy.Require(_access, Permission.ManageUsers);
            if (string.IsNullOrWhiteSpace(username))
            {
                return Result.Fail<long>("Le nom d'utilisateur est requis.", "User_UsernameRequired");
            }

            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            {
                return Result.Fail<long>(PasswordTooShortMessage, "User_PasswordTooShort");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (uow.Users.GetByUsername(username.Trim()) != null)
                {
                    return Result.Fail<long>("Ce nom d'utilisateur existe déjà.", "User_UsernameExists");
                }

                string salt = NewSalt();
                var user = new User
                {
                    Username = username.Trim(),
                    FullName = fullName,
                    Salt = salt,
                    PasswordHash = HashEncoded(password, salt, CurrentIterations),
                    Role = role,
                    Department = department,
                    IsActive = true,
                    MustChangePassword = mustChangePassword
                };

                long id = uow.Users.Insert(user);
                Audit.Record("User", id, AuditAction.Created, "Utilisateur créé : " + user.Username + " (" + role + ")");
                return Result.Ok(id);
            }
        }

        public Result Update(User user)
        {
            AccessPolicy.Require(_access, Permission.ManageUsers);
            if (user == null || user.Id <= 0)
            {
                return Result.Fail("Utilisateur introuvable.", "User_NotFound");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                User existing = uow.Users.GetById(user.Id);
                if (existing == null)
                {
                    return Result.Fail("Utilisateur introuvable.", "User_NotFound");
                }

                // Never leave the system without an active administrator.
                bool losingAdmin = existing.Role == UserRole.Admin && existing.IsActive &&
                                   (user.Role != UserRole.Admin || !user.IsActive);
                if (losingAdmin && uow.Users.CountAdmins() <= 1)
                {
                    return Result.Fail("Au moins un administrateur actif est requis.", "User_LastAdmin");
                }

                bool wasActive = existing.IsActive;
                existing.FullName = user.FullName;
                existing.Role = user.Role;
                existing.Department = user.Department;
                existing.IsActive = user.IsActive;
                uow.Users.Update(existing);
                Audit.Record("User", existing.Id, AuditAction.Updated, "Utilisateur modifié : " + existing.Username,
                    wasActive ? "actif" : "inactif", user.IsActive ? "actif" : "inactif");
                return Result.Ok();
            }
        }

        [OptiPaie.Core.Security.WriteGuardExempt("Self-service password change (including the forced must-change flow) must work for any signed-in user; an administrator reset of another user is reached only through the admin UI.")]
        public Result ChangePassword(long userId, string newPassword, bool mustChangePassword = false)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
            {
                return Result.Fail(PasswordTooShortMessage, "User_PasswordTooShort");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                User existing = uow.Users.GetById(userId);
                if (existing == null)
                {
                    return Result.Fail("Utilisateur introuvable.", "User_NotFound");
                }

                existing.Salt = NewSalt();
                existing.PasswordHash = HashEncoded(newPassword, existing.Salt, CurrentIterations);
                existing.MustChangePassword = mustChangePassword;
                uow.Users.Update(existing);
                Audit.Record("User", userId, AuditAction.Updated,
                    mustChangePassword
                        ? "Mot de passe réinitialisé (à changer à la prochaine connexion) : " + existing.Username
                        : "Mot de passe modifié : " + existing.Username);
                return Result.Ok();
            }
        }

        public Result Delete(long userId)
        {
            AccessPolicy.Require(_access, Permission.ManageUsers);
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                User existing = uow.Users.GetById(userId);
                if (existing == null)
                {
                    return Result.Ok();
                }

                if (existing.Role == UserRole.Admin && existing.IsActive && uow.Users.CountAdmins() <= 1)
                {
                    return Result.Fail("Impossible de supprimer le dernier administrateur.", "User_LastAdmin");
                }

                uow.Users.SoftDelete(userId);
                Audit.Record("User", userId, AuditAction.Deleted, "Utilisateur supprimé : " + existing.Username);
                return Result.Ok();
            }
        }

        public User Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Users.GetById(id);
            }
        }

        public IReadOnlyList<User> GetAll()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Users.GetAll().ToList();
            }
        }

        [OptiPaie.Core.Security.WriteGuardExempt("Authentication (login): must be reachable by any caller; it verifies credentials and does not perform a privileged write.")]
        public Result<User> Authenticate(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                return Result.Fail<User>("Identifiants invalides.", "User_BadCredentials");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                // Look up regardless of active state so a disabled account with the RIGHT password
                // gets a precise "account disabled" reason instead of "wrong password".
                User user = uow.Users.GetByUsernameIncludingInactive(username.Trim());
                if (user == null || !Verify(password, user.Salt, user.PasswordHash))
                {
                    return Result.Fail<User>("Nom d'utilisateur ou mot de passe incorrect.", "User_BadCredentials");
                }

                if (!user.IsActive)
                {
                    return Result.Fail<User>("Ce compte est désactivé.", "User_Disabled");
                }

                // Transparent upgrade: a hash written at an older (lower) cost is re-hashed at the
                // current cost now that we hold the correct clear password. Never changes the flags.
                if (NeedsRehash(user.PasswordHash))
                {
                    user.Salt = NewSalt();
                    user.PasswordHash = HashEncoded(password, user.Salt, CurrentIterations);
                    uow.Users.Update(user);
                }

                return Result.Ok(user);
            }
        }

        public int ActiveUserCount()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Users.CountActive();
            }
        }

        public bool HasActiveAdmin()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Users.CountAdmins() > 0;
            }
        }

        public bool IsLoginRequired()
        {
            return IsLoginEnabled() && ActiveUserCount() > 0;
        }

        public bool IsLoginEnabled()
        {
            return string.Equals(_settings.Get(AuthEnabledKey, "false"), "true", StringComparison.OrdinalIgnoreCase);
        }

        public void SetLoginEnabled(bool enabled)
        {
            AccessPolicy.Require(_access, Permission.ManageUsers);
            // Never allow enabling the gate with no active administrator — that would raise a login
            // screen no one can pass (a lock-out). Disabling is always allowed.
            if (enabled && !HasActiveAdmin())
            {
                return;
            }

            _settings.Set(AuthEnabledKey, enabled ? "true" : "false");
            Audit.Record("Settings", 0, AuditAction.StatusChanged,
                enabled ? "Connexion au démarrage activée" : "Connexion au démarrage désactivée");
        }

        // -- hashing -----------------------------------------------------------

        private static string NewSalt()
        {
            var bytes = new byte[SaltBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Raw PBKDF2-SHA256 derivation to a Base64 hash.</summary>
        private static string ComputeHash(string password, string saltBase64, int iterations)
        {
            byte[] salt = Convert.FromBase64String(saltBase64);
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return Convert.ToBase64String(pbkdf2.GetBytes(HashBytes));
            }
        }

        /// <summary>Stored form: "iterations$hash" so the cost travels with the hash.</summary>
        private static string HashEncoded(string password, string saltBase64, int iterations)
        {
            return iterations.ToString(CultureInfo.InvariantCulture) + "$" + ComputeHash(password, saltBase64, iterations);
        }

        /// <summary>
        /// Splits a stored hash into its iteration count and Base64 hash. A legacy value with no
        /// "iterations$" prefix is assumed to be the old 100k-iteration hash.
        /// </summary>
        private static void Decode(string stored, out int iterations, out string hashBase64)
        {
            iterations = LegacyIterations;
            hashBase64 = stored ?? string.Empty;
            if (string.IsNullOrEmpty(stored))
            {
                return;
            }

            int sep = stored.IndexOf('$');
            if (sep > 0 &&
                int.TryParse(stored.Substring(0, sep), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                parsed > 0)
            {
                iterations = parsed;
                hashBase64 = stored.Substring(sep + 1);
            }
        }

        private static bool NeedsRehash(string storedHash)
        {
            Decode(storedHash, out int iterations, out _);
            return iterations != CurrentIterations;
        }

        private static bool Verify(string password, string saltBase64, string storedHash)
        {
            if (string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            Decode(storedHash, out int iterations, out string expectedHashBase64);
            if (string.IsNullOrEmpty(expectedHashBase64))
            {
                return false;
            }

            string actual = ComputeHash(password, saltBase64, iterations);

            // Constant-time comparison.
            byte[] a = Convert.FromBase64String(actual);
            byte[] b;
            try { b = Convert.FromBase64String(expectedHashBase64); }
            catch (FormatException) { return false; }

            if (a.Length != b.Length)
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
    }
}
