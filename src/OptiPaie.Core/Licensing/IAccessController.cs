namespace OptiPaie.Core.Licensing
{
    /// <summary>How the application may currently be used.</summary>
    public enum AccessState
    {
        /// <summary>No valid license and no (started) trial — activation required.</summary>
        NotActivated = 0,

        /// <summary>A valid, active license — full access.</summary>
        Licensed = 1,

        /// <summary>Running inside the trial period.</summary>
        Trial = 2,

        /// <summary>The trial was started and has expired — activation required.</summary>
        TrialExpired = 3,

        /// <summary>A license exists but is suspended/revoked/expired — activation required.</summary>
        Locked = 4
    }

    /// <summary>The combined access decision (license + trial) shown to the shell.</summary>
    public sealed class AccessEvaluation
    {
        public AccessEvaluation(AccessState state, LicenseSnapshot license, TrialInfo trial)
        {
            State = state;
            License = license;
            Trial = trial;
        }

        public AccessState State { get; }
        public LicenseSnapshot License { get; }
        public TrialInfo Trial { get; }

        /// <summary>True when the app may be used right now (licensed or in trial).</summary>
        public bool CanUseApp => State == AccessState.Licensed || State == AccessState.Trial;
    }

    /// <summary>
    /// Decides whether the application can be used, combining the license state and
    /// the trial. Used at startup to choose between the activation window and the
    /// main window, and by the Settings/License page.
    /// </summary>
    public interface IAccessController
    {
        AccessEvaluation Evaluate();
    }
}
