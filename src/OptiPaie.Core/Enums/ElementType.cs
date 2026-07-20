namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// Whether a payroll element adds to or subtracts from the payslip.
    /// </summary>
    public enum ElementType
    {
        /// <summary>A gain (earning) that adds to the salary, e.g. salaire de base, primes.</summary>
        Gain = 1,

        /// <summary>A deduction (retenue) that subtracts, e.g. absence, acompte, avance.</summary>
        Deduction = 2
    }
}
