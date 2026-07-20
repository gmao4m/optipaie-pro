namespace OptiPaie.Core.Enums
{
    /// <summary>Severity of a payroll engine message.</summary>
    public enum PayrollMessageSeverity
    {
        /// <summary>Informational note (e.g. a rule that was applied).</summary>
        Info = 1,

        /// <summary>A non-blocking caution the user should review.</summary>
        Warning = 2,

        /// <summary>A blocking problem; the calculation did not proceed or is invalid.</summary>
        Error = 3
    }
}
