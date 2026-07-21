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
            IContractService contracts)
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
        }

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
    }
}
