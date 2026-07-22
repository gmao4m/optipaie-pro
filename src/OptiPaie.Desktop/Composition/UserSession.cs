using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// The current login session. When no login is enforced (<see cref="Current"/> is null),
    /// the single operator has full administrator access — so nothing is locked down unless
    /// an admin explicitly enables login and signs in.
    /// </summary>
    public sealed class UserSession
    {
        /// <summary>The signed-in user, or null when login is not enforced.</summary>
        public User Current { get; set; }

        public bool IsAuthenticated => Current != null;

        /// <summary>Full access: either no login is enforced, or the signed-in user is an admin.</summary>
        public bool IsAdmin => Current == null || Current.Role == UserRole.Admin;

        /// <summary>A signed-in manager (scoped to their department).</summary>
        public bool IsManager => Current != null && Current.Role == UserRole.Manager;

        /// <summary>The manager's department scope, or null (= all).</summary>
        public string Department => Current != null ? Current.Department : null;

        public string DisplayName =>
            Current == null ? "Administrateur"
            : (string.IsNullOrWhiteSpace(Current.FullName) ? Current.Username : Current.FullName);

        public string RoleLabel =>
            Current == null ? "Accès complet"
            : (Current.Role == UserRole.Admin ? "Administrateur" : "Responsable");
    }
}
