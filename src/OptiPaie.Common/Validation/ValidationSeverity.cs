namespace OptiPaie.Common.Validation
{
    /// <summary>
    /// Severity of a validation finding.
    /// </summary>
    public enum ValidationSeverity
    {
        /// <summary>A blocking problem; the operation must not proceed.</summary>
        Error = 1,

        /// <summary>A non-blocking caution the user should review.</summary>
        Warning = 2
    }
}
