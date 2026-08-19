using System;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services.Auth
{
    /// <summary>Which credential the single login screen matched.</summary>
    public enum LoginPrincipal { None, Owner, LocalUser }

    /// <summary>Why a login attempt failed (mapped to an Arabic message by the UI).</summary>
    public enum LoginFailure { None, MissingIdentifier, MissingPassword, BadCredentials, Disabled, Technical }

    /// <summary>Result of one unified login attempt.</summary>
    public sealed class LoginOutcome
    {
        private LoginOutcome() { }

        public bool Success { get; private set; }
        public LoginPrincipal Principal { get; private set; }

        /// <summary>The authenticated local user (only for <see cref="LoginPrincipal.LocalUser"/>).</summary>
        public User User { get; private set; }

        public LoginFailure Failure { get; private set; }

        public static LoginOutcome Ok(LoginPrincipal principal, User user = null) =>
            new LoginOutcome { Success = true, Principal = principal, User = user, Failure = LoginFailure.None };

        public static LoginOutcome Fail(LoginFailure failure) =>
            new LoginOutcome { Success = false, Principal = LoginPrincipal.None, Failure = failure };
    }

    /// <summary>
    /// Single, unified authentication behind the ONE login screen. The user types ONE
    /// identifier — their owner email OR a local username — and this coordinator detects
    /// which it is: an identifier that matches the owner email always routes to the owner
    /// account (an <b>always-available fallback</b>, independent of the local users), any
    /// other identifier is checked against the local user accounts.
    /// <para>
    /// It is pure and side-effect-scoped: on success it opens the owner session (a Core
    /// interface) and RETURNS the authenticated local user for the caller to seat in the UI
    /// session — so the whole decision is unit-testable with no WPF/session dependency.
    /// Any unexpected error is reported as <see cref="LoginFailure.Technical"/>, never thrown,
    /// so the login screen can show it instead of crashing.
    /// </para>
    /// </summary>
    public sealed class LoginCoordinator
    {
        private readonly ICustomerAccountService _owner;
        private readonly IUserService _users;

        public LoginCoordinator(ICustomerAccountService owner, IUserService users)
        {
            _owner = owner;                                   // may be null on installs with no owner account
            _users = users ?? throw new ArgumentNullException(nameof(users));
        }

        public LoginOutcome Login(string identifier, string password)
        {
            string id = (identifier ?? string.Empty).Trim();
            if (id.Length == 0) return LoginOutcome.Fail(LoginFailure.MissingIdentifier);
            if (string.IsNullOrEmpty(password)) return LoginOutcome.Fail(LoginFailure.MissingPassword);

            try
            {
                // Owner account (email) — reachable whatever happens to the local users list.
                if (_owner != null && _owner.HasAccount &&
                    string.Equals(id, _owner.Email, StringComparison.OrdinalIgnoreCase))
                {
                    return _owner.SignIn(id, password)
                        ? LoginOutcome.Ok(LoginPrincipal.Owner)
                        : LoginOutcome.Fail(LoginFailure.BadCredentials);
                }

                // Local user (username).
                Result<User> r = _users.Authenticate(id, password);
                if (r.IsSuccess)
                {
                    return LoginOutcome.Ok(LoginPrincipal.LocalUser, r.Value);
                }

                return r.ErrorCode == "User_Disabled"
                    ? LoginOutcome.Fail(LoginFailure.Disabled)
                    : LoginOutcome.Fail(LoginFailure.BadCredentials);
            }
            catch
            {
                return LoginOutcome.Fail(LoginFailure.Technical);
            }
        }
    }
}
