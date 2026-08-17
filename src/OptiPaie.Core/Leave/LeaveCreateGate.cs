namespace OptiPaie.Core.Leave
{
    /// <summary>
    /// The pure decision behind the "New request" button: it can open the form only when a company is
    /// active AND that company has at least one active employee. When it cannot, a stable reason code is
    /// returned so the UI shows a PERSISTENT explanation instead of a fleeting pop-up (or a dead button).
    /// Kept free of WPF so it is directly testable.
    /// </summary>
    public static class LeaveCreateGate
    {
        public static bool CanCreate(bool hasActiveCompany, int activeEmployeeCount)
        {
            return hasActiveCompany && activeEmployeeCount > 0;
        }

        /// <summary>Empty when creation is allowed; otherwise the reason key to display and log.</summary>
        public static string ReasonCode(bool hasActiveCompany, int activeEmployeeCount)
        {
            if (!hasActiveCompany) return "Leave_NeedCompany";
            if (activeEmployeeCount <= 0) return "Leave_NeedEmployee";
            return string.Empty;
        }
    }
}
