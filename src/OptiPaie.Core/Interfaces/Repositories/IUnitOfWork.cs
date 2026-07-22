using System;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Coordinates a single database connection and an optional transaction across
    /// all repositories, so a multi-table operation (e.g. saving a payroll run with
    /// its payslips and details) commits atomically. Dispose closes the connection.
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>Company repository.</summary>
        ICompanyRepository Companies { get; }

        /// <summary>Employee repository.</summary>
        IEmployeeRepository Employees { get; }

        /// <summary>Payroll element repository.</summary>
        IPayrollElementRepository PayrollElements { get; }

        /// <summary>Employee element assignment repository.</summary>
        IEmployeeElementRepository EmployeeElements { get; }

        /// <summary>Payroll run repository.</summary>
        IPayrollRunRepository PayrollRuns { get; }

        /// <summary>Payslip repository.</summary>
        IPayslipRepository Payslips { get; }

        /// <summary>Payroll detail repository.</summary>
        IPayrollDetailRepository PayrollDetails { get; }

        /// <summary>Archive document repository.</summary>
        IArchiveDocumentRepository ArchiveDocuments { get; }

        /// <summary>Legal parameter repository.</summary>
        ILegalParameterRepository LegalParameters { get; }

        /// <summary>Application settings repository.</summary>
        IAppSettingRepository AppSettings { get; }

        /// <summary>Language repository.</summary>
        ILanguageRepository Languages { get; }

        /// <summary>Backup record repository.</summary>
        IBackupRecordRepository BackupRecords { get; }

        /// <summary>Attendance repository (premium module, shares Employees).</summary>
        IAttendanceRepository Attendance { get; }

        /// <summary>Leave repository (premium module, shares Employees).</summary>
        ILeaveRepository Leave { get; }

        /// <summary>Loan repository (premium module, shares Employees).</summary>
        ILoanRepository Loans { get; }

        /// <summary>Contract repository (premium module, shares Employees).</summary>
        IContractRepository Contracts { get; }

        /// <summary>Performance-review repository (premium module, shares Employees).</summary>
        IPerformanceRepository Performance { get; }

        /// <summary>Asset repository (premium module, shares Employees/Companies).</summary>
        IAssetRepository Assets { get; }

        /// <summary>Training repository (premium module, shares Employees/Companies).</summary>
        ITrainingRepository Training { get; }

        /// <summary>Recruitment/ATS repository (premium module; creates shared Employees on hire).</summary>
        IAtsRepository Ats { get; }

        /// <summary>Work-certificate repository (premium module, shares Employees).</summary>
        IWorkCertificateRepository Certificates { get; }

        /// <summary>Append-only audit trail.</summary>
        IAuditRepository Audit { get; }

        /// <summary>Begins a database transaction for the subsequent repository calls.</summary>
        void BeginTransaction();

        /// <summary>Commits the active transaction.</summary>
        void Commit();

        /// <summary>Rolls back the active transaction.</summary>
        void Rollback();
    }
}
