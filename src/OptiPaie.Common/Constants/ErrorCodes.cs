namespace OptiPaie.Common.Constants
{
    /// <summary>
    /// Stable error/validation keys emitted by the service layer. The presentation
    /// layer resolves each to a localized (Arabic/French) message. Keeping them as
    /// constants prevents drift between producers and resources.
    /// </summary>
    public static class ErrorCodes
    {
        // Generic
        public const string NotFound = "Error_NotFound";

        // Company
        public const string CompanyNameRequired = "Company_NameRequired";

        // Employee
        public const string EmployeeCompanyRequired = "Employee_CompanyRequired";
        public const string EmployeeCompanyNotFound = "Employee_CompanyNotFound";
        public const string EmployeeLastNameRequired = "Employee_LastNameRequired";
        public const string EmployeeFirstNameRequired = "Employee_FirstNameRequired";
        public const string EmployeeBaseSalaryInvalid = "Employee_BaseSalaryInvalid";
        public const string EmployeeHireDateRequired = "Employee_HireDateRequired";
        public const string EmployeeExitBeforeHire = "Employee_ExitBeforeHire";

        // Payroll element
        public const string ElementNameRequired = "Element_NameRequired";
        public const string ElementBaseRequired = "Element_BaseRequired";
        public const string ElementSystemCannotDelete = "Element_SystemCannotDelete";

        // Employee element assignment
        public const string EmployeeElementEmployeeNotFound = "EmployeeElement_EmployeeNotFound";
        public const string EmployeeElementElementNotFound = "EmployeeElement_ElementNotFound";
        public const string EmployeeElementDuplicate = "EmployeeElement_Duplicate";

        // Backup / restore
        public const string BackupFailed = "Backup_Failed";
        public const string BackupFileNotFound = "Backup_FileNotFound";
        public const string BackupInvalidFile = "Backup_InvalidFile";
        public const string RestoreFailed = "Restore_Failed";
    }
}
