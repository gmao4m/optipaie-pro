using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A local application user. Passwords are never stored in clear — only a PBKDF2 hash
    /// and its per-user salt. Login is entirely optional (see the auth-enabled setting); a
    /// fresh install has no users and runs open.
    /// </summary>
    public sealed class User : EntityBase
    {
        public string Username { get; set; }

        public string FullName { get; set; }

        /// <summary>Base64 PBKDF2 hash of the password.</summary>
        public string PasswordHash { get; set; }

        /// <summary>Base64 per-user salt.</summary>
        public string Salt { get; set; }

        public UserRole Role { get; set; } = UserRole.Manager;

        /// <summary>Department a Manager is scoped to (null / empty = all).</summary>
        public string Department { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
