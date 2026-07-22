using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Local user accounts, roles and the optional login gate. Passwords are hashed with
    /// PBKDF2 (SHA-256, 100k iterations) and a per-user salt — the clear password is never
    /// stored. The gate stays off until explicitly enabled, so existing/demo installs run open.
    /// </summary>
    public sealed class UserService : IUserService
    {
        private const string AuthEnabledKey = "Auth.Enabled";
        private const int Iterations = 100000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly ISettingsService _settings;

        public UserService(IUnitOfWorkFactory unitOfWorkFactory, ISettingsService settings)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _settings = Guard.AgainstNull(settings, nameof(settings));
        }

        public Result<long> Create(string username, string fullName, string password, UserRole role, string department)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return Result.Fail<long>("Le nom d'utilisateur est requis.", "User_UsernameRequired");
            }

            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                return Result.Fail<long>("Le mot de passe doit contenir au moins 4 caractères.", "User_PasswordTooShort");
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
                    PasswordHash = Hash(password, salt),
                    Role = role,
                    Department = department,
                    IsActive = true
                };

                long id = uow.Users.Insert(user);
                return Result.Ok(id);
            }
        }

        public Result Update(User user)
        {
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

                existing.FullName = user.FullName;
                existing.Role = user.Role;
                existing.Department = user.Department;
                existing.IsActive = user.IsActive;
                uow.Users.Update(existing);
                return Result.Ok();
            }
        }

        public Result ChangePassword(long userId, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
            {
                return Result.Fail("Le mot de passe doit contenir au moins 4 caractères.", "User_PasswordTooShort");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                User existing = uow.Users.GetById(userId);
                if (existing == null)
                {
                    return Result.Fail("Utilisateur introuvable.", "User_NotFound");
                }

                existing.Salt = NewSalt();
                existing.PasswordHash = Hash(newPassword, existing.Salt);
                uow.Users.Update(existing);
                return Result.Ok();
            }
        }

        public Result Delete(long userId)
        {
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

        public Result<User> Authenticate(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                return Result.Fail<User>("Identifiants invalides.", "User_BadCredentials");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                User user = uow.Users.GetByUsername(username.Trim());
                if (user == null || !Verify(password, user.Salt, user.PasswordHash))
                {
                    return Result.Fail<User>("Nom d'utilisateur ou mot de passe incorrect.", "User_BadCredentials");
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
            _settings.Set(AuthEnabledKey, enabled ? "true" : "false");
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

        private static string Hash(string password, string saltBase64)
        {
            byte[] salt = Convert.FromBase64String(saltBase64);
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                return Convert.ToBase64String(pbkdf2.GetBytes(HashBytes));
            }
        }

        private static bool Verify(string password, string saltBase64, string expectedHashBase64)
        {
            if (string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(expectedHashBase64))
            {
                return false;
            }

            string actual = Hash(password, saltBase64);
            // Constant-time comparison.
            byte[] a = Convert.FromBase64String(actual);
            byte[] b = Convert.FromBase64String(expectedHashBase64);
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
