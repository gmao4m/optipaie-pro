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

                // Seeing an identified employee's pay: every role EXCEPT read-only (Admin already
                // returned true above). This is the single door-independent salary gate.
                case Permission.ViewSalary:
                    return role != UserRole.ReadOnly;

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

                // Payroll / payslips & salary data: visible to payroll and accountant, VIEW-only to
                // HR — but NEVER to read-only (it loses payroll entirely).
                case "payroll":
                    return role != UserRole.ReadOnly;

                // CNAS declarations: payroll & accountant only. Not HR, and NOT read-only (which
                // loses CNAS entirely).
                case "cnas":
                    return role == UserRole.Payroll || role == UserRole.Accountant;

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
                    return true; // viewable by every role (dashboard, employees, attendance,
                                 // leave, loans, contracts, attestations, work_certificate, archive, reports)
            }
        }

        /// <summary>
        /// Whether a role may PRINT or EXPORT payslip / payroll (salary) data — the "P" column of the
        /// payroll row. Export is guarded exactly as strictly as viewing: a role that cannot see a
        /// payslip can never export it. HR may VIEW payroll (<see cref="CanOpen"/>) but may NOT export
        /// it; read-only sees no payroll at all. Administrators, payroll and accountant may export.
        /// </summary>
        public static bool CanExportPayroll(UserRole role) =>
            role == UserRole.Admin || role == UserRole.Payroll || role == UserRole.Accountant;

        /// <summary>
        /// Whether a role may PRINT or EXPORT CNAS declaration files — the "P" column of the CNAS row,
        /// identical to the set that may view CNAS (administrators, payroll, accountant). HR and
        /// read-only can neither see nor export CNAS.
        /// </summary>
        public static bool CanExportCnas(UserRole role) =>
            role == UserRole.Admin || role == UserRole.Payroll || role == UserRole.Accountant;

        /// <summary>
        /// Whether a role may see an identified employee's PAY, through any door — the single salary
        /// gate (<see cref="Permission.ViewSalary"/>). Every role except read-only holds it.
        /// </summary>
        public static bool CanSeeSalary(UserRole role) => IsAllowed(role, Permission.ViewSalary);

        /// <summary>
        /// The write permission that governs a module's create/edit/delete actions, or null for a
        /// read-only module (dashboard, archive, reports, attestations). The single map the UI uses to
        /// decide whether to show the create/edit/delete buttons on a screen — the SAME permissions the
        /// services enforce.
        /// </summary>
        public static Permission? EditPermissionFor(string moduleKey)
        {
            switch (moduleKey)
            {
                case "companies": return Permission.ManageCompanies;
                case "settings": return Permission.ManageSettings;
                case "users": return Permission.ManageUsers;
                case "employees": return Permission.EditEmployees;
                case "payroll-elements": return Permission.ManagePayrollElements;
                case "payroll": return Permission.RunPayroll;
                case "cnas": return Permission.ManageCnas;
                case "attendance": return Permission.ManageAttendance;
                case "leave": return Permission.ManageLeave;
                case "loans": return Permission.ManageLoans;
                case "performance": return Permission.ManagePerformance;
                case "contracts": return Permission.ManageContracts;
                case "training": return Permission.ManageTraining;
                case "assets": return Permission.ManageAssets;
                case "work_certificate": return Permission.ManageCertificates;
                case "ats":
                case "recruitment": return Permission.ManageRecruitment;
                default: return null; // dashboard, archive, reports, attestations — read-only screens
            }
        }

        /// <summary>
        /// Whether a role may CREATE / EDIT / DELETE on a module — the single source the UI uses to
        /// show or hide those buttons, identical to what the service layer enforces. A read-only
        /// module (no edit permission) is never editable by anyone through this path.
        /// </summary>
        public static bool CanEdit(UserRole role, string moduleKey)
        {
            Permission? p = EditPermissionFor(moduleKey);
            return p.HasValue && IsAllowed(role, p.Value);
        }

        /// <summary>
        /// Whether a role may PRINT / EXPORT a module's documents — the single source the UI uses to
        /// show or hide the print/export buttons. Payslip (salary) and CNAS exports follow their strict
        /// rule; every other module's documents are readable (printable) by any role that can open it.
        /// </summary>
        public static bool CanExport(UserRole role, string moduleKey)
        {
            switch (moduleKey)
            {
                case "payroll": return CanExportPayroll(role);
                case "cnas": return CanExportCnas(role);
                default: return CanOpen(role, moduleKey); // reads/prints follow view for every other module
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
