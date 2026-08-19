namespace OptiPaie.Services.Auth
{
    /// <summary>
    /// Maps a <see cref="LoginFailure"/> to the localization KEY of the message shown on the
    /// login screen — a pure presentation mapping, testable and independent of WPF. It changes
    /// no authentication logic: the grant/deny decision is made elsewhere; this only decides
    /// which Arabic message to display for a given failure.
    /// <para>
    /// A generic "bad credentials" is split for clarity into "unknown identifier" vs "wrong
    /// password" using an <paramref name="identifierExists"/> flag the caller computes with a
    /// read-only lookup (owner email / local username) — again, no effect on who may sign in.
    /// </para>
    /// </summary>
    public static class LoginMessageMap
    {
        public static string KeyFor(LoginFailure failure, bool identifierExists)
        {
            switch (failure)
            {
                case LoginFailure.MissingIdentifier: return "Login_Err_NeedUsername";
                case LoginFailure.MissingPassword: return "Login_Err_NeedPassword";
                case LoginFailure.Disabled: return "Login_Err_Disabled";
                case LoginFailure.Technical: return "Login_Err_Technical";
                case LoginFailure.BadCredentials:
                    return identifierExists ? "Login_Err_WrongPassword" : "Login_Err_UnknownIdentifier";
                default:
                    return "Login_Err_UnknownIdentifier";
            }
        }
    }
}
