namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// The single, local customer account that drives login/logout on THIS machine —
    /// created once at first activation (email + company + password) and then verified
    /// entirely offline. It is deliberately independent of both the license (which gates
    /// whether the app may run at all) and the optional multi-user login feature.
    /// <para>
    /// The password is stored only as a salted PBKDF2 hash (never in clear), wrapped with
    /// DPAPI so a copied database cannot be brute-forced on another machine. Because it is
    /// fully local, sign-in after a logout works with no Internet and never re-asks for a
    /// license key.
    /// </para>
    /// </summary>
    public interface ICustomerAccountService
    {
        /// <summary>True once an account has been created on this installation.</summary>
        bool HasAccount { get; }

        /// <summary>True while a session is active (the user has not signed out).</summary>
        bool IsSignedIn { get; }

        /// <summary>The account email (also stamped on the license), or empty.</summary>
        string Email { get; }

        /// <summary>The company name entered at sign-up, or empty.</summary>
        string Company { get; }

        /// <summary>
        /// Creates (or replaces) the local account and opens a session immediately.
        /// Called after a license is successfully activated.
        /// </summary>
        void Register(string email, string company, string password);

        /// <summary>
        /// Verifies email + password against the stored account and, on success, opens a
        /// session. Returns false on any mismatch. Fully offline.
        /// </summary>
        bool SignIn(string email, string password);

        /// <summary>Ends the session but keeps the account (and the license) intact.</summary>
        void SignOut();

        /// <summary>Removes the account entirely (rarely needed).</summary>
        void Clear();
    }
}
