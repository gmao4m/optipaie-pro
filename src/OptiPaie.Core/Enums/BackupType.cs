namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// How a database backup was produced.
    /// </summary>
    public enum BackupType
    {
        /// <summary>Triggered explicitly by the user.</summary>
        Manual = 1,

        /// <summary>Created automatically (scheduled or before a migration).</summary>
        Automatic = 2
    }
}
