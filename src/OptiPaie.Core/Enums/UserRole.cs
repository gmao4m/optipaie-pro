namespace OptiPaie.Core.Enums
{
    /// <summary>Access role of a local user account.</summary>
    public enum UserRole
    {
        /// <summary>Full access to every module and to administration (companies, settings, users).</summary>
        Admin = 1,

        /// <summary>Scoped access: the HR modules for their own department; no administration.</summary>
        Manager = 2
    }
}
