using System;
using System.Data.SQLite;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Repositories;

namespace OptiPaie.Data.Context
{
    /// <summary>
    /// Owns a single SQLite connection and an optional transaction, and lazily
    /// exposes the repositories that share them. Multi-table operations are made
    /// atomic by wrapping them in <see cref="BeginTransaction"/>/<see cref="Commit"/>.
    /// Disposing rolls back any open transaction and closes the connection.
    /// </summary>
    internal sealed class UnitOfWork : IUnitOfWork
    {
        private readonly SQLiteConnection _connection;
        private SQLiteTransaction _transaction;
        private bool _disposed;

        private ICompanyRepository _companies;
        private IEmployeeRepository _employees;
        private IPayrollElementRepository _payrollElements;
        private IEmployeeElementRepository _employeeElements;
        private IPayrollRunRepository _payrollRuns;
        private IPayslipRepository _payslips;
        private IPayrollDetailRepository _payrollDetails;
        private IArchiveDocumentRepository _archiveDocuments;
        private ILegalParameterRepository _legalParameters;
        private IAppSettingRepository _appSettings;
        private ILanguageRepository _languages;
        private IBackupRecordRepository _backupRecords;
        private IAttendanceRepository _attendance;
        private ILeaveRepository _leave;
        private ILoanRepository _loans;
        private IContractRepository _contracts;

        public UnitOfWork(SQLiteConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>The shared open connection (used by the repositories).</summary>
        internal SQLiteConnection Connection => _connection;

        /// <summary>The active transaction, or null.</summary>
        internal SQLiteTransaction Transaction => _transaction;

        public ICompanyRepository Companies => _companies ?? (_companies = new CompanyRepository(this));
        public IEmployeeRepository Employees => _employees ?? (_employees = new EmployeeRepository(this));
        public IPayrollElementRepository PayrollElements => _payrollElements ?? (_payrollElements = new PayrollElementRepository(this));
        public IEmployeeElementRepository EmployeeElements => _employeeElements ?? (_employeeElements = new EmployeeElementRepository(this));
        public IPayrollRunRepository PayrollRuns => _payrollRuns ?? (_payrollRuns = new PayrollRunRepository(this));
        public IPayslipRepository Payslips => _payslips ?? (_payslips = new PayslipRepository(this));
        public IPayrollDetailRepository PayrollDetails => _payrollDetails ?? (_payrollDetails = new PayrollDetailRepository(this));
        public IArchiveDocumentRepository ArchiveDocuments => _archiveDocuments ?? (_archiveDocuments = new ArchiveDocumentRepository(this));
        public ILegalParameterRepository LegalParameters => _legalParameters ?? (_legalParameters = new LegalParameterRepository(this));
        public IAppSettingRepository AppSettings => _appSettings ?? (_appSettings = new AppSettingRepository(this));
        public ILanguageRepository Languages => _languages ?? (_languages = new LanguageRepository(this));
        public IBackupRecordRepository BackupRecords => _backupRecords ?? (_backupRecords = new BackupRecordRepository(this));
        public IAttendanceRepository Attendance => _attendance ?? (_attendance = new AttendanceRepository(this));

        public ILeaveRepository Leave => _leave ?? (_leave = new LeaveRepository(this));

        public ILoanRepository Loans => _loans ?? (_loans = new LoanRepository(this));

        public IContractRepository Contracts => _contracts ?? (_contracts = new ContractRepository(this));

        public void BeginTransaction()
        {
            if (_transaction != null)
            {
                throw new InvalidOperationException("A transaction is already in progress.");
            }

            _transaction = _connection.BeginTransaction();
        }

        public void Commit()
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("There is no transaction to commit.");
            }

            try
            {
                _transaction.Commit();
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void Rollback()
        {
            if (_transaction == null)
            {
                return;
            }

            try
            {
                _transaction.Rollback();
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_transaction != null)
            {
                try
                {
                    _transaction.Rollback();
                }
                catch
                {
                    // Disposing must not throw; a failed rollback is non-recoverable here.
                }

                _transaction.Dispose();
                _transaction = null;
            }

            _connection.Dispose();
            _disposed = true;
        }
    }
}
