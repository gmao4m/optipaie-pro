namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// The pure startup / reconnection gate decision, kept out of the WPF layer so it is
    /// unit-testable. It answers one question — may the app open right now without asking
    /// for a license? — from two facts: whether access is currently usable, and whether
    /// this machine has ever been activated.
    /// </summary>
    public static class AccessDecision
    {
        /// <summary>
        /// True when the app may open WITHOUT prompting for a license: it is usable now
        /// (a valid license OR an active trial), OR the machine was already activated at
        /// least once (the trusted marker). Trusting the marker is what stops a transient
        /// local re-verification failure from re-asking an already-activated poste.
        /// </summary>
        public static bool MayOpen(bool canUseApp, bool isActivated) => canUseApp || isActivated;

        /// <summary>
        /// The inverse gate: activation must be requested only for a machine that is neither
        /// usable now nor ever activated (i.e. a genuinely fresh, never-activated install).
        /// </summary>
        public static bool MustActivate(bool canUseApp, bool isActivated) => !canUseApp && !isActivated;
    }
}
