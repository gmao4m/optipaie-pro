namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Encrypts/decrypts small secrets at rest (the license token, the license key
    /// and the trial state) so the local cache is confidential and bound to the
    /// current user account. Implemented with Windows DPAPI. Returns null on failure
    /// rather than throwing, so a corrupted/foreign cache is simply treated as absent.
    /// </summary>
    public interface ILocalCipher
    {
        /// <summary>Encrypts a UTF-8 string to a base64 blob, or null.</summary>
        string Protect(string plainText);

        /// <summary>Decrypts a base64 blob back to the UTF-8 string, or null.</summary>
        string Unprotect(string protectedBase64);
    }
}
