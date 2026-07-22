namespace OptiPaie.Core.Enums
{
    /// <summary>The kind of entry on an employee's career timeline.</summary>
    public enum CareerEventType
    {
        /// <summary>A change of position (old -> new).</summary>
        Promotion = 1,

        /// <summary>A bonus or reward (amount + category).</summary>
        Reward = 2
    }
}
