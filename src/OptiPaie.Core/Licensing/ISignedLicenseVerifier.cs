namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Verifies a signed license token offline using the embedded public key, and
    /// returns its payload only when the signature is valid. This is what makes the
    /// local cache tamper-proof: editing it breaks the signature.
    /// </summary>
    public interface ISignedLicenseVerifier
    {
        /// <summary>
        /// Returns true and the decoded payload when the token's signature is valid;
        /// false (payload null) for any malformed, tampered or unverifiable token.
        /// </summary>
        bool TryVerify(string token, out SignedLicensePayload payload);
    }
}
