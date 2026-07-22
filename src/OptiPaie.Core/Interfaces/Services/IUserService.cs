using System.Collections.Generic;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Local user accounts and the optional login gate. Passwords are PBKDF2-hashed. Login
    /// is dormant until an admin both creates a user and enables it, so a fresh/demo install
    /// keeps running open. Never touches payroll.
    /// </summary>
    public interface IUserService
    {
        /// <summary>Creates a user with a hashed password. Username must be unique.</summary>
        Result<long> Create(string username, string fullName, string password, UserRole role, string department);

        /// <summary>Updates a user's name, role, department and active flag (not the password).</summary>
        Result Update(User user);

        /// <summary>Sets a new password (re-hashed).</summary>
        Result ChangePassword(long userId, string newPassword);

        /// <summary>Soft-deletes a user (never the last active admin).</summary>
        Result Delete(long userId);

        User Get(long id);

        IReadOnlyList<User> GetAll();

        /// <summary>Verifies the credentials; returns the user on success.</summary>
        Result<User> Authenticate(string username, string password);

        /// <summary>Number of active users.</summary>
        int ActiveUserCount();

        /// <summary>True when a login should be enforced (the gate is enabled AND a user exists).</summary>
        bool IsLoginRequired();

        /// <summary>Whether the login gate is switched on.</summary>
        bool IsLoginEnabled();

        /// <summary>Turns the login gate on/off (only meaningful once at least one admin exists).</summary>
        void SetLoginEnabled(bool enabled);
    }
}
