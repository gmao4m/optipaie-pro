namespace OptiPaie.Core.Enums
{
    /// <summary>Daily attendance state of an employee (Algerian HR practice).</summary>
    public enum AttendanceStatus
    {
        /// <summary>Présent — worked the day normally.</summary>
        Present = 1,

        /// <summary>Absent — unjustified absence (payroll-impacting).</summary>
        Absent = 2,

        /// <summary>Retard — present but arrived after the tolerance.</summary>
        Late = 3,

        /// <summary>Congé — on approved leave.</summary>
        Leave = 4,

        /// <summary>Jour férié — public holiday.</summary>
        Holiday = 5,

        /// <summary>Repos — weekly rest day.</summary>
        Rest = 6
    }
}
