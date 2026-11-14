using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Security
{
    /// <summary>
    /// The current caller's authorization context, consulted by the service layer. Supplied by the
    /// shell from the signed-in session. When the login gate is off (the default for existing
    /// installs), <see cref="IsEnforced"/> is false and every operation is allowed — the app runs
    /// exactly as before. When on, <see cref="CurrentRole"/> drives <see cref="AccessPolicy"/>.
    /// </summary>
    public interface IAccessContext
    {
        /// <summary>True only when a login is being enforced (gate on). False = open, full access.</summary>
        bool IsEnforced { get; }

        /// <summary>The signed-in user's role (only meaningful when <see cref="IsEnforced"/> is true).</summary>
        UserRole CurrentRole { get; }

        /// <summary>
        /// True when the signed-in principal is the account OWNER (the customer account), not merely
        /// an administrator user. Gates the few owner-only operations (e.g. database restore).
        /// </summary>
        bool IsOwner { get; }
    }
}
