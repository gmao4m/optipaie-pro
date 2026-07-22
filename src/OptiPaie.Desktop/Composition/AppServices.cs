using OptiPaie.Common.Configuration;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Updates;
using OptiPaie.Localization;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// The composed application services for the WPF client. Same shape as the original
    /// composition, but without the DevExpress reporting service — documents are now
    /// produced by the WPF layer. Everything is exposed through interfaces.
    /// </summary>
    public sealed class AppServices
    {
        public AppServices(
            AppConfiguration configuration,
            ILogger logger,
            ICompanyService companies,
            IEmployeeService employees,
            IPayrollElementService payrollElements,
            IConfigurationService configurationService,
            ISettingsService settings,
            IArchiveService archive,
            IBackupService backup,
            IPayrollService payroll,
            ILocalizationService localization,
            IModuleRegistry modules,
            ILicensingService licensing,
            ILicenseGate licenseGate,
            ITrialService trial,
            IAccessController access,
            IUpdateService update,
            IAttendanceService attendance,
            ILeaveService leave,
            ILoanService loans,
            IContractService contracts,
            IPerformanceService performance,
            IAssetService assets,
            ITrainingService training,
            IAtsService ats,
            IWorkCertificateService certificates,
            IDashboardService dashboard,
            IReportService reports,
            INotificationService notifications,
            IAuditService audit)
        {
            Configuration = configuration;
            Logger = logger;
            Companies = companies;
            Employees = employees;
            PayrollElements = payrollElements;
            ConfigurationService = configurationService;
            Settings = settings;
            Archive = archive;
            Backup = backup;
            Payroll = payroll;
            Localization = localization;
            Modules = modules;
            Licensing = licensing;
            LicenseGate = licenseGate;
            Trial = trial;
            Access = access;
            Update = update;
            Attendance = attendance;
            Leave = leave;
            Loans = loans;
            Contracts = contracts;
            Performance = performance;
            Assets = assets;
            Training = training;
            Ats = ats;
            Certificates = certificates;
            Dashboard = dashboard;
            Reports = reports;
            Notifications = notifications;
            Audit = audit;
        }

        private CompanyContext _companyContext;

        /// <summary>
        /// The app-wide active-company selection shared by the header selector and every
        /// module. Created lazily from the company service so no composition change is needed.
        /// </summary>
        public CompanyContext CompanyContext => _companyContext ?? (_companyContext = new CompanyContext(Companies));

        public AppConfiguration Configuration { get; }
        public ILogger Logger { get; }
        public ICompanyService Companies { get; }
        public IEmployeeService Employees { get; }
        public IPayrollElementService PayrollElements { get; }
        public IConfigurationService ConfigurationService { get; }
        public ISettingsService Settings { get; }
        public IArchiveService Archive { get; }
        public IBackupService Backup { get; }
        public IPayrollService Payroll { get; }
        public ILocalizationService Localization { get; }

        /// <summary>Catalogue of licensable modules (nav is generated from this).</summary>
        public IModuleRegistry Modules { get; }

        /// <summary>Licensing orchestrator (activation, synchronization, offline verification).</summary>
        public ILicensingService Licensing { get; }

        /// <summary>Read-only gate the UI uses to unlock or lock (🔒) each module.</summary>
        public ILicenseGate LicenseGate { get; }

        /// <summary>Offline 30-day trial manager.</summary>
        public ITrialService Trial { get; }

        /// <summary>Combined access decision (license + trial) for startup gating.</summary>
        public IAccessController Access { get; }

        /// <summary>Auto-update service (Velopack / GitHub Releases).</summary>
        public IUpdateService Update { get; }

        /// <summary>Attendance module (premium) — shares Employees/Companies.</summary>
        public IAttendanceService Attendance { get; }

        /// <summary>Leave module (premium) — writes approved days into Attendance.</summary>
        public ILeaveService Leave { get; }

        /// <summary>Loans module (premium) — feeds instalments into payroll as deductions.</summary>
        public ILoanService Loans { get; }

        /// <summary>Contracts module (premium) — syncs the shared employee's terms.</summary>
        public IContractService Contracts { get; }

        /// <summary>Performance module (premium) — pulls attendance context live.</summary>
        public IPerformanceService Performance { get; }

        /// <summary>Assets module (premium) — company property assigned to shared employees.</summary>
        public IAssetService Assets { get; }

        /// <summary>Training module (premium) — sessions with shared-employee enrolments.</summary>
        public ITrainingService Training { get; }

        /// <summary>Recruitment/ATS module (premium) — creates the shared employee on hire.</summary>
        public IAtsService Ats { get; }

        /// <summary>Work-certificates module (premium) — renders live from the shared records.</summary>
        public IWorkCertificateService Certificates { get; }

        /// <summary>Executive dashboard aggregation across every HR module (read-only).</summary>
        public IDashboardService Dashboard { get; }

        /// <summary>Reports Center — cross-module report library (read-only).</summary>
        public IReportService Reports { get; }

        /// <summary>Central notification engine — cross-module alerts for the bell (read-only).</summary>
        public INotificationService Notifications { get; }

        /// <summary>Audit trail — records lifecycle changes and serves history/activity feeds.</summary>
        public IAuditService Audit { get; }
    }
}
