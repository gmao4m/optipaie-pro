using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Security
{
    /// <summary>
    /// The one explicit, intentional "no enforcement" access context. It exists so that the rare,
    /// legitimate case of running a write WITHOUT a signed-in session is a deliberate, greppable
    /// decision — never the accidental result of a forgotten/ null context (which now fails closed).
    /// <para>
    /// <b>Production rule:</b> the ONLY production code allowed to use <see cref="Unrestricted"/> is a
    /// first-run bootstrap that must create the very first account before any user/role exists. In
    /// the current architecture no such production call site exists (first-run admin creation runs
    /// through the real session context with the login gate still off, i.e. not enforced), so this
    /// sentinel is used only by tests. A suite test (<c>UnrestrictedAccessGuardTests</c>) FAILS the
    /// build if it ever appears in a production file. Tests may use it freely.
    /// </para>
    /// </summary>
    public sealed class AccessContext : IAccessContext
    {
        /// <summary>The explicit, intentional unrestricted context (see the type remarks).</summary>
        public static readonly IAccessContext Unrestricted = new AccessContext();

        private AccessContext() { }

        public bool IsEnforced => false;

        public UserRole CurrentRole => UserRole.Admin;

        public bool IsOwner => true;

        public long? CurrentUserId => null;
    }
}
