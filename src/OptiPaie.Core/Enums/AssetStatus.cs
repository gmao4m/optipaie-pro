namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle state of a company asset.</summary>
    public enum AssetStatus
    {
        /// <summary>In stock, ready to be assigned.</summary>
        Available = 1,

        /// <summary>Currently held by an employee.</summary>
        Assigned = 2,

        /// <summary>Out for repair / maintenance.</summary>
        UnderRepair = 3,

        /// <summary>Retired / written off — no longer assignable.</summary>
        Retired = 4
    }
}
