namespace OptiPaie.Core.Updates
{
    /// <summary>
    /// The pure decision that turns a release-check + metadata into an update outcome.
    /// Blocks downgrades (only a strictly-newer version is offered). Fully testable.
    /// </summary>
    public static class UpdatePolicy
    {
        public static AppUpdateCheck Evaluate(
            string appName, string currentVersion, string latestVersion, bool mandatory, string releaseNotes)
        {
            // No candidate, or not strictly newer than the current version → no update
            // (this is also the downgrade guard).
            if (string.IsNullOrWhiteSpace(latestVersion) ||
                !AppVersion.IsNewer(latestVersion, currentVersion))
            {
                return AppUpdateCheck.None(appName, currentVersion);
            }

            return new AppUpdateCheck(true, appName, currentVersion, latestVersion, releaseNotes, mandatory);
        }
    }
}
