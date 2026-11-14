namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// Access role of a local user account. Values are stable (persisted as integers); new roles
    /// are appended, never renumbered. <see cref="Manager"/> is a backward-compatible alias for the
    /// former single non-admin role, now expressed as <see cref="Hr"/>, so existing role=2 data and
    /// older code keep working.
    /// </summary>
    public enum UserRole
    {
        /// <summary>Full access to every module and to administration (companies, settings, users).</summary>
        Admin = 1,

        /// <summary>Human-resources: employees, contracts, leave, attendance, training, performance,
        /// assets, certificates, recruitment; no administration; cannot edit payroll figures.</summary>
        Hr = 2,

        /// <summary>Payroll manager: payroll runs, CNAS declarations, loans, elements; no administration.</summary>
        Payroll = 3,

        /// <summary>Accountant: mostly read + print/export of payroll, CNAS, loans, reports, archive.</summary>
        Accountant = 4,

        /// <summary>Read-only: may view and print, never edit.</summary>
        ReadOnly = 5,

        /// <summary>Backward-compatible alias for the former non-admin role (= <see cref="Hr"/>).</summary>
        Manager = Hr
    }
}
