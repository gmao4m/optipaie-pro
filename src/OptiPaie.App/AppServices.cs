using OptiPaie.Common.Configuration;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Localization;
using OptiPaie.Reporting.Engine;

namespace OptiPaie.App
{
    /// <summary>
    /// The composed application services, built once by <see cref="CompositionRoot"/>
    /// and handed to the UI. Exposes everything through interfaces, so the UI depends
    /// on abstractions, not concrete implementations.
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
            IReportService reports,
            ILicenseService license,
            ILocalizationService localization)
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
            Reports = reports;
            License = license;
            Localization = localization;
        }

        /// <summary>Machine-level bootstrap configuration (paths, default language).</summary>
        public AppConfiguration Configuration { get; }

        /// <summary>Application logger.</summary>
        public ILogger Logger { get; }

        /// <summary>Company management service.</summary>
        public ICompanyService Companies { get; }

        /// <summary>Employee management service.</summary>
        public IEmployeeService Employees { get; }

        /// <summary>Payroll element catalog service.</summary>
        public IPayrollElementService PayrollElements { get; }

        /// <summary>Legal/configuration service (builds the engine's legal snapshot).</summary>
        public IConfigurationService ConfigurationService { get; }

        /// <summary>UI/application preferences service.</summary>
        public ISettingsService Settings { get; }

        /// <summary>Archive search and reprint service.</summary>
        public IArchiveService Archive { get; }

        /// <summary>Backup and restore service.</summary>
        public IBackupService Backup { get; }

        /// <summary>Payroll generation service (orchestrates the engine).</summary>
        public IPayrollService Payroll { get; }

        /// <summary>Payslip report service (preview + PDF).</summary>
        public IReportService Reports { get; }

        /// <summary>License manager (offline).</summary>
        public ILicenseService License { get; }

        /// <summary>Localization (language/RTL) service.</summary>
        public ILocalizationService Localization { get; }
    }
}
