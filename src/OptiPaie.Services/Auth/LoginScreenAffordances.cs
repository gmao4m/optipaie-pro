namespace OptiPaie.Services.Auth
{
    /// <summary>
    /// The affordances the ONE unified login screen must present <b>permanently</b> — never
    /// conditioned on a failed attempt — so a blocked user is never stranded:
    /// <list type="bullet">
    /// <item>a « الدخول بمفتاح الترخيص » escape to the activation screen, so an owner who
    /// forgot their password can re-activate with the license key and regain access;</item>
    /// <item>a « التحقق من التحديثات » action, because the login screen is exactly where a
    /// blocked client needs to update to unblock themselves.</item>
    /// </list>
    /// The login view binds its escape link and update button to these, and tests assert they
    /// stay always-on. (Dropping either was a v1.27.0 regression; this makes it machine-checked.)
    /// </summary>
    public static class LoginScreenAffordances
    {
        /// <summary>The license-key escape to activation is always shown on the login screen.</summary>
        public const bool LicenseKeyEscapeAlwaysVisible = true;

        /// <summary>The "check for updates" action is always shown on the login screen.</summary>
        public const bool UpdateCheckAlwaysVisible = true;
    }
}
