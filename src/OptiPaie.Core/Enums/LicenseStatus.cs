namespace OptiPaie.Core.Enums
{
    /// <summary>State of the application license.</summary>
    public enum LicenseStatus
    {
        /// <summary>No license activated.</summary>
        NotActivated = 0,

        /// <summary>A valid license is active.</summary>
        Active = 1,

        /// <summary>The license has expired.</summary>
        Expired = 2,

        /// <summary>Running in evaluation/trial mode.</summary>
        Trial = 3
    }
}
