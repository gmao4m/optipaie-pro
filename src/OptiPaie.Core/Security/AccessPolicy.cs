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
            // Owner-only operations are never granted by a role (not even Admin) — they are checked
            // via IAccessContext.IsOwner through RequireOwner.
            if (permission == Permission.RestoreDatabase)
            {
                return false;
            }

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
        /// Enforces <paramref name="permission"/> for the given context. <b>Fails closed:</b> a null
        /// context is NOT "no enforcement" — it is a wiring omission and is DENIED. A caller that
        /// legitimately means "no enforcement" must say so explicitly with
        /// <see cref="AccessContext.Unrestricted"/>. When a context is present but not enforcing (the
        /// login gate is off, an open install), everything is allowed; when it is enforcing, the
        /// role must hold the permission.
        /// </summary>
        public static void Require(IAccessContext context, Permission permission)
        {
            if (context == null)
            {
                // Fail closed: never let a missing access context silently permit a write.
                throw new AccessDeniedException(permission);
            }

            if (!context.IsEnforced)
            {
                return;
            }

            if (!IsAllowed(context.CurrentRole, permission))
            {
                throw new AccessDeniedException(permission);
            }
        }

        /// <summary>
        /// Enforces an OWNER-only operation (e.g. database restore). Fails closed on a null context;
        /// allows an unenforced (open) install; otherwise requires <see cref="IAccessContext.IsOwner"/>
        /// — an administrator user is NOT the owner.
        /// </summary>
        public static void RequireOwner(IAccessContext context)
        {
            if (context == null)
            {
                throw new AccessDeniedException(Permission.RestoreDatabase);
            }

            if (!context.IsEnforced)
            {
                return;
            }

            if (!context.IsOwner)
            {
                throw new AccessDeniedException(Permission.RestoreDatabase);
            }
        }
    }
}
