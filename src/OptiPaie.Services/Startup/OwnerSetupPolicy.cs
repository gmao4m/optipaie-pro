namespace OptiPaie.Services.Startup
{
    /// <summary>What the first-launch-after-upgrade path must do for this installation.</summary>
    public enum OwnerSetupAction
    {
        /// <summary>Nothing to do: the owner is already established, OR this is a brand-new empty
        /// install whose owner is created by the normal activation/registration flow.</summary>
        None,

        /// <summary>A customer account already exists (an activated install from before the permission
        /// system) — adopt it, in place, as the OWNER. Its password is preserved; no data is touched.</summary>
        AdoptExistingAccount,

        /// <summary>There is real business data but no account at all — the customer must create the
        /// OWNER account before anything else is reachable.</summary>
        CreateOwnerRequired
    }

    /// <summary>
    /// The single, pure decision for the first launch after the permission system is introduced.
    /// It never touches data; it only classifies the installation so the app can either adopt the
    /// existing owner, walk the customer through creating one, or do nothing. Fully unit-testable.
    /// </summary>
    public static class OwnerSetupPolicy
    {
        /// <param name="ownerEstablished">The one-time owner setup has already completed on this machine.</param>
        /// <param name="hasAccount">A local customer account (email + password) already exists.</param>
        /// <param name="hasBusinessData">The database already holds real business data (e.g. a company).</param>
        public static OwnerSetupAction Decide(bool ownerEstablished, bool hasAccount, bool hasBusinessData)
        {
            // Already done once — never run again, never surprise a returning customer.
            if (ownerEstablished)
            {
                return OwnerSetupAction.None;
            }

            // An activated install from before the permission system: the existing account IS the
            // owner. Adopt it silently and keep its password — never force a re-creation.
            if (hasAccount)
            {
                return OwnerSetupAction.AdoptExistingAccount;
            }

            // Real data but no account at all (a very old install that never registered an account):
            // the owner MUST be created before the app opens, so the data is never left unprotected.
            if (hasBusinessData)
            {
                return OwnerSetupAction.CreateOwnerRequired;
            }

            // A fresh, empty install (trial / first run): the activation/registration flow creates the
            // owner in the normal course — the upgrade path has nothing to do here.
            return OwnerSetupAction.None;
        }
    }
}
