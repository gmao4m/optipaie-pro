namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Canonical module keys for the Payroll product. These strings are the single
    /// source of truth shared by the cloud backend, the admin panel and this
    /// application (see backend/MODULES.md). NEVER rename a key once shipped — it
    /// would orphan already-issued licenses. To retire a module, disable it.
    /// </summary>
    public static class ModuleKeys
    {
        /// <summary>Base product — always enabled while the license is active.</summary>
        public const string Payroll = "payroll";

        public const string Ats = "ats";
        public const string Attendance = "attendance";
        public const string Leave = "leave";
        public const string Loans = "loans";
        public const string Performance = "performance";
        public const string Contracts = "contracts";
        public const string Training = "training";
        public const string Assets = "assets";
        public const string WorkCertificate = "work_certificate";
    }
}
