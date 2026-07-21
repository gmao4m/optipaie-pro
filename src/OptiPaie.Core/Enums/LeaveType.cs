namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// Types of leave recognised by Algerian labour law (loi 90-11). Paid types keep
    /// the salary intact; unpaid ones are deducted, which is why the distinction is
    /// carried all the way into attendance and payroll.
    /// </summary>
    public enum LeaveType
    {
        /// <summary>Congé annuel payé — 2,5 days per month worked, capped at 30/year.</summary>
        Annual = 1,

        /// <summary>Congé de maladie (covered by CNAS, not deducted here).</summary>
        Sick = 2,

        /// <summary>Congé sans solde — deducted from the salary.</summary>
        Unpaid = 3,

        /// <summary>Congé de maternité.</summary>
        Maternity = 4,

        /// <summary>Congés exceptionnels (événements familiaux), paid.</summary>
        Special = 5
    }
}
