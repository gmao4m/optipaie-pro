namespace OptiPaie.Core.Security
{
    /// <summary>
    /// A guarded, write/administration capability. Reads (viewing, printing, exporting) are open to
    /// every role, so only state-changing operations carry a permission. A service declares the one
    /// it requires and the single <see cref="AccessPolicy"/> choke point decides.
    /// </summary>
    public enum Permission
    {
        // Administration — administrators only.
        ManageCompanies,
        ManageSettings,
        ManageUsers,

        // Human-resources writes.
        EditEmployees,
        ManageLeave,
        ManageContracts,
        ManagePerformance,
        ManageTraining,
        ManageAssets,
        ManageCertificates,
        ManageRecruitment,

        // Payroll writes.
        RunPayroll,
        ManageCnas,
        ManageLoans,
        ManagePayrollElements,

        // Shared between HR and payroll.
        ManageAttendance,

        // Owner-only (checked via IAccessContext.IsOwner, not by role): overwriting the whole
        // database from a backup. No role grants it.
        RestoreDatabase
    }
}
