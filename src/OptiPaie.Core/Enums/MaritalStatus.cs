namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// The employee's marital situation. Recorded for HR/payslip purposes; the
    /// post-2022 IRG barème does not depend on it.
    /// </summary>
    public enum MaritalStatus
    {
        /// <summary>Single (célibataire).</summary>
        Single = 1,

        /// <summary>Married (marié(e)).</summary>
        Married = 2,

        /// <summary>Divorced (divorcé(e)).</summary>
        Divorced = 3,

        /// <summary>Widowed (veuf/veuve).</summary>
        Widowed = 4
    }
}
