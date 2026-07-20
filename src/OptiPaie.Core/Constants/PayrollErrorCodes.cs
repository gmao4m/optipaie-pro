namespace OptiPaie.Core.Constants
{
    /// <summary>
    /// Stable keys for messages produced by the payroll engine (validation,
    /// verification, notices). Resolved to localized text by the presentation layer.
    /// </summary>
    public static class PayrollErrorCodes
    {
        public const string ContextMissing = "Payroll_ContextMissing";
        public const string PeriodInvalid = "Payroll_PeriodInvalid";
        public const string LegalMissing = "Payroll_LegalMissing";
        public const string ElementsMissing = "Payroll_ElementsMissing";
        public const string BaseSalaryNegative = "Payroll_BaseSalaryNegative";
        public const string WorkedDaysInvalid = "Payroll_WorkedDaysInvalid";
        public const string DuplicateElement = "Payroll_DuplicateElement";
        public const string InvalidPercentage = "Payroll_InvalidPercentage";
        public const string NegativeAmount = "Payroll_NegativeAmount";
        public const string UnsupportedCalculationMethod = "Payroll_UnsupportedCalculationMethod";
        public const string UnsupportedCalculationBase = "Payroll_UnsupportedCalculationBase";
        public const string MissingCalculationBase = "Payroll_MissingCalculationBase";
        public const string NegativeTaxableBase = "Payroll_NegativeTaxableBase";
        public const string NetNegative = "Payroll_NetNegative";
        public const string LissageReferenceMissing = "Payroll_LissageReferenceMissing";

        // Service-level (orchestration) checks
        public const string CompanyNotFound = "Payroll_CompanyNotFound";
        public const string EmployeeNotFound = "Payroll_EmployeeNotFound";
        public const string PeriodFuture = "Payroll_PeriodFuture";
        public const string DuplicatePayroll = "Payroll_DuplicatePayroll";
        public const string EmployeeNotActive = "Payroll_EmployeeNotActive";
        public const string PersistFailed = "Payroll_PersistFailed";
        public const string RequestMissing = "Payroll_RequestMissing";
    }
}
