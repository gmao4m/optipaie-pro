using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Security;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// Bridges the signed-in <see cref="UserSession"/> to the service-layer <see cref="IAccessContext"/>.
    /// Evaluated lazily at each call so it reflects the session as it is at that moment. Enforcement
    /// is active only while the login gate is on AND a user exists (same rule as the UI gate); the
    /// owner session (no local user) and an open install both resolve to full administrator access.
    /// Created early (before the session exists) and <see cref="Bind"/>-ed once both do, so the
    /// service graph can be wired without reordering.
    /// </summary>
    public sealed class SessionAccessContext : IAccessContext
    {
        private UserSession _session;
        private IUserService _users;
        private ICustomerAccountService _owner;

        public void Bind(UserSession session, IUserService users, ICustomerAccountService owner)
        {
            _session = session;
            _users = users;
            _owner = owner;
        }

        public bool IsEnforced => _users != null && _users.IsLoginRequired();

        public UserRole CurrentRole =>
            _session != null && _session.Current != null ? _session.Current.Role : UserRole.Admin;

        // The owner is the signed-in customer account (no local user seated). An administrator USER
        // is not the owner.
        public bool IsOwner =>
            (_session == null || _session.Current == null) && _owner != null && _owner.IsSignedIn;

        public long? CurrentUserId =>
            _session != null && _session.Current != null ? _session.Current.Id : (long?)null;
    }
}
