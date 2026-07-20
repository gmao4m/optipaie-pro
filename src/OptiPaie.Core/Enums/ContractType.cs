namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// The employee's contract type.
    /// </summary>
    public enum ContractType
    {
        /// <summary>Contrat à durée indéterminée (permanent).</summary>
        Cdi = 1,

        /// <summary>Contrat à durée déterminée (fixed-term).</summary>
        Cdd = 2,

        /// <summary>Apprenticeship contract.</summary>
        Apprenticeship = 3,

        /// <summary>Internship / pré-emploi.</summary>
        Internship = 4,

        /// <summary>Any other contractual arrangement.</summary>
        Other = 99
    }
}
