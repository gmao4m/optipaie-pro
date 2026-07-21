namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle of an employment contract.</summary>
    public enum ContractStatus
    {
        /// <summary>Prepared but not yet in force — changes nothing on the employee.</summary>
        Draft = 1,

        /// <summary>In force — its salary, type and position drive the shared employee record.</summary>
        Active = 2,

        /// <summary>A fixed-term contract whose end date has passed.</summary>
        Expired = 3,

        /// <summary>Ended early (démission, licenciement, rupture) — sets the employee's exit date.</summary>
        Terminated = 4,

        /// <summary>Superseded by a renewal contract.</summary>
        Renewed = 5
    }
}
