namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Remembers, on THIS machine, that a license activation was completed at least once.
    /// <para>
    /// Once a poste is activated, the startup / reconnection gate TRUSTS this marker and
    /// never re-asks for the license — a transient LOCAL re-verification problem (device-id
    /// drift, an unreadable cache, a momentary read error) can no longer lock out a paying
    /// customer at the door. Server-confirmed expiry / revocation are still surfaced by the
    /// background synchronisation; they are not enforced by blocking the login screen.
    /// </para>
    /// <para>
    /// A machine that has NEVER activated has no marker, so it stays blocked: the license
    /// check is <b>moved</b> (to the first activation), never removed.
    /// </para>
    /// </summary>
    public interface IActivationState
    {
        /// <summary>True once this machine has completed (or inherited) a license activation.</summary>
        bool IsActivated { get; }

        /// <summary>Records that this machine is activated. Idempotent; safe to call repeatedly.</summary>
        void MarkActivated();
    }
}
