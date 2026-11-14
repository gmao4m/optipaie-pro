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
        /// Whether a role may OPEN (view) a module/screen — the "V" column of the role matrix, and
        /// the single source the UI navigation uses so a visible entry can never disagree with what
        /// the service layer allows. Administrators open everything; viewing (read) is open to every
        /// role except for the admin-only and domain-restricted screens listed here.
        /// </summary>
        public static bool CanOpen(UserRole role, string moduleKey)
        {
            if (role == UserRole.Admin)
            {
                return true;
            }

            switch (moduleKey)
            {
                // Administration — administrators only.
                case "companies":
                case "settings":
                case "users":
                    return false;

                // CNAS declarations: not HR.
                case "cnas":
                    return role != UserRole.Hr;

                // Performance & recruitment: HR domain + read-only; not payroll/accountant.
                case "performance":
                case "ats":          // the recruitment module key
                case "recruitment":
                    return role == UserRole.Hr || role == UserRole.ReadOnly;

                // Training & assets: everyone except payroll.
                case "training":
                case "assets":
                    return role != UserRole.Payroll;

                default:
                    return true; // viewable by every role (dashboard, employees, payroll, attendance,
                                 // leave, loans, contracts, attestations, work_certificate, archive, reports)
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
