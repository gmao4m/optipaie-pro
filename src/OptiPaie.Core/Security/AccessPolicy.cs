using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Security
{
    /// <summary>
    /// The single, pure decision point that maps a role to the write/administration permissions it
    /// holds — the one place the whole app agrees on "who may change what". Administrators may do
    /// everything; HR and payroll each own their domain; accountant and read-only never write.
    /// Fully unit-testable with no dependencies.
    /// </summary>
    public static class AccessPolicy
    {
        public static bool IsAllowed(UserRole role, Permission permission)
        {
            if (role == UserRole.Admin)
            {
                return true; // administrators have every permission
            }

            switch (permission)
            {
                // Administration is administrators-only (already returned true above).
                case Permission.ManageCompanies:
                case Permission.ManageSettings:
                case Permission.ManageUsers:
                    return false;

                // Human-resources domain.
                case Permission.EditEmployees:
                case Permission.ManageLeave:
                case Permission.ManageContracts:
                case Permission.ManagePerformance:
                case Permission.ManageTraining:
                case Permission.ManageAssets:
                case Permission.ManageCertificates:
                case Permission.ManageRecruitment:
                    return role == UserRole.Hr;

                // Payroll domain.
                case Permission.RunPayroll:
                case Permission.ManageCnas:
                case Permission.ManageLoans:
                case Permission.ManagePayrollElements:
                    return role == UserRole.Payroll;

                // Shared between HR and payroll.
                case Permission.ManageAttendance:
                    return role == UserRole.Hr || role == UserRole.Payroll;

                default:
                    return false; // Accountant / ReadOnly: no write permissions
            }
        }

        /// <summary>
        /// Enforces <paramref name="permission"/> for the given context. No-op when the context is
        /// absent or the login gate is off (open install). Throws <see cref="AccessDeniedException"/>
        /// when a login is enforced and the current role is not permitted.
        /// </summary>
        public static void Require(IAccessContext context, Permission permission)
        {
            if (context == null || !context.IsEnforced)
            {
                return;
            }

            if (!IsAllowed(context.CurrentRole, permission))
            {
                throw new AccessDeniedException(permission);
            }
        }
    }
}
