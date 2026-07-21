namespace OptiPaie.Core.Enums
{
    /// <summary>Category of a company asset handed to employees.</summary>
    public enum AssetCategory
    {
        /// <summary>Ordinateur portable.</summary>
        Laptop = 1,

        /// <summary>Téléphone / SIM.</summary>
        Phone = 2,

        /// <summary>Véhicule de service.</summary>
        Vehicle = 3,

        /// <summary>Tenue / EPI (uniforme, équipement de protection).</summary>
        Uniform = 4,

        /// <summary>Outillage.</summary>
        Tool = 5,

        /// <summary>Autre matériel.</summary>
        Other = 99
    }
}
