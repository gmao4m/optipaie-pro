namespace OptiPaie.Core.Licensing
{
    /// <summary>Why an owner-password recovery attempt failed.</summary>
    public enum RecoveryFailure
    {
        None,
        NoAccount,
        Locked,        // too many recent failed attempts
        BadCode,
        WeakPassword
    }

    /// <summary>
    /// Outcome of an owner-password recovery with a one-time code. On success it carries the NEW
    /// recovery code (the old one is consumed and must be replaced), shown to the user once.
    /// </summary>
    public sealed class RecoveryResult
    {
        private RecoveryResult() { }

        public bool Success { get; private set; }

        /// <summary>The freshly issued one-time recovery code (success only) — show once, store safely.</summary>
        public string NewRecoveryCode { get; private set; }

        public RecoveryFailure Failure { get; private set; }

        public static RecoveryResult Ok(string newCode) =>
            new RecoveryResult { Success = true, NewRecoveryCode = newCode, Failure = RecoveryFailure.None };

        public static RecoveryResult Fail(RecoveryFailure failure) =>
            new RecoveryResult { Success = false, Failure = failure };
    }
}
