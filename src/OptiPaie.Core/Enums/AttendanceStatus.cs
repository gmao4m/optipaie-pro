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

        /// <summary>Repos — weekly rest day (weekend).</summary>
        Rest = 6,

        /// <summary>Mission — en déplacement / mission professionnelle (worked, paid).</summary>
        Mission = 7,

        /// <summary>Absence justifiée — justified absence (does NOT reduce pay).</summary>
        JustifiedAbsence = 8,

        /// <summary>Demi-journée — half day worked (reduces pay by 0.5 day).</summary>
        HalfDay = 9,

        /// <summary>Travail un jour de repos / férié — worked a rest day or holiday (paid).</summary>
        RestWorked = 10
    }
}
