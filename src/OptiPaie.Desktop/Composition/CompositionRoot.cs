using System.IO;
using OptiPaie.Common.Configuration;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Interfaces;
using OptiPaie.Core.Interfaces.Engine;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Updates;
using OptiPaie.Data.Backup;
using OptiPaie.Data.Context;
using OptiPaie.Data.Licensing;
using OptiPaie.Desktop.Updates;
using OptiPaie.Localization;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Licensing;
using OptiPaie.Services.Updates;
using OptiPaie.Services.Validation;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// Assembles the object graph for the WPF client (manual constructor injection),
    /// mirroring the original composition but excluding the DevExpress report service.
    /// Reads configuration, initialises the SQLite database and constructs the services.
    /// </summary>
    public static class CompositionRoot
    {
        public static AppServices Build()
        {
            AppConfiguration configuration = AppConfigurationReader.Read();

            ILogger logger = new FileLogger(Path.Combine(configuration.DataDirectory, "Logs", AppConstants.LogFileName));

            var bootstrapper = new DatabaseBootstrapper(configuration);
            SqliteConnectionFactory connectionFactory = bootstrapper.Initialize();

            IUnitOfWorkFactory unitOfWorkFactory = new UnitOfWorkFactory(connectionFactory);
            IDatabaseBackupProvider backupProvider = new SqliteBackupProvider(connectionFactory, configuration);

            var companyService = new CompanyService(unitOfWorkFactory, new CompanyValidator());
            var employeeService = new EmployeeService(unitOfWorkFactory, new EmployeeValidator());
            var elementService = new PayrollElementService(unitOfWorkFactory, new PayrollElementValidator());
            var configurationService = new ConfigurationService(unitOfWorkFactory);
            var settingsService = new SettingsService(unitOfWorkFactory);
            var archiveService = new ArchiveService(unitOfWorkFactory);
            var attendanceService = new AttendanceService(unitOfWorkFactory);
            var leaveService = new LeaveService(unitOfWorkFactory);
            var loanService = new LoanService(unitOfWorkFactory);
            var contractService = new ContractService(unitOfWorkFactory);
            var performanceService = new PerformanceService(unitOfWorkFactory, attendanceService);
            var assetService = new AssetService(unitOfWorkFactory);
            var trainingService = new TrainingService(unitOfWorkFactory);
            var atsService = new AtsService(unitOfWorkFactory);
            var certificateService = new WorkCertificateService(unitOfWorkFactory);
            var dashboardService = new DashboardService(
                companyService, employeeService, contractService, leaveService, loanService,
                attendanceService, atsService, assetService, trainingService);
            var reportService = new ReportService(
                companyService, employeeService, attendanceService, leaveService, loanService,
                atsService, assetService, trainingService);
            var notificationService = new NotificationService(
                companyService, employeeService, contractService, leaveService, trainingService, performanceService);

            // Audit trail. Wired into the lifecycle events of the modules through the
            // optional sink, so history is recorded without changing any service ctor.
            var auditService = new AuditService(unitOfWorkFactory, logger);
            leaveService.Audit = auditService;
            contractService.Audit = auditService;
            loanService.Audit = auditService;
            assetService.Audit = auditService;
            var backupService = new BackupService(backupProvider, unitOfWorkFactory, configuration, logger);
            var localizationService = new LocalizationService();
            var userService = new UserService(unitOfWorkFactory, settingsService);

            IPayrollEngine engine = new PayrollCalculationEngine();
            var payrollService = new PayrollService(unitOfWorkFactory, configurationService, engine);

            // Licensing (provider-agnostic). Only SupabaseLicenseBackend knows the
            // backend is Supabase; everything else depends on the interfaces. No
            // network I/O happens here — the service only reads/verifies the local
            // cache, so startup stays instant and fully offline-capable.
            LicensingOptions licensingOptions = LicensingConfig.Build();
            IModuleRegistry moduleRegistry = new ModuleRegistry();
            ISignedLicenseVerifier verifier = new Ed25519LicenseVerifier(licensingOptions, logger);
            IDeviceIdentity deviceIdentity = new WmiDeviceIdentity();
            ILocalCipher cipher = new DpapiLocalCipher(logger);
            ILicenseBackend licenseBackend = new SupabaseLicenseBackend(licensingOptions, logger);
            ILicenseStore licenseStore = new SqliteLicenseStore(connectionFactory);
            ITrialStore trialStore = new SqliteTrialStore(connectionFactory);
            ITrialService trialService = new TrialService(trialStore, cipher, logger);
            ILicensingService licensingService = new LicensingService(
                licenseBackend, verifier, deviceIdentity, licenseStore, cipher, licensingOptions, logger);
            ILicenseGate licenseGate = new LicenseGate(licensingService, trialService);
            IAccessController accessController = new AccessGate(licensingService, trialService);

            // Auto-update (Velopack). The channel is the only Velopack-aware piece; the
            // service + metadata source are provider-agnostic and unit-tested. Disabled
            // gracefully (IsSupported=false) on non-installed/dev runs or when unconfigured.
            UpdateOptions updateOptions = UpdateConfig.Build(licensingOptions);
            // GitHub Releases is the preferred update source; the Velopack feed is the
            // fallback when only a feed URL is configured. Both are IReleaseChannel.
            IReleaseChannel releaseChannel = !string.IsNullOrWhiteSpace(updateOptions.GitHubRepo)
                ? (IReleaseChannel)new GitHubReleaseChannel(updateOptions, logger)
                : new VelopackReleaseChannel(updateOptions, logger);
            IUpdateMetadataSource updateMetadata = new SupabaseUpdateMetadataSource(updateOptions, logger);
            IUpdateService updateService = new UpdateService(releaseChannel, updateMetadata, updateOptions, logger);

            return new AppServices(
                configuration,
                logger,
                companyService,
                employeeService,
                elementService,
                configurationService,
                settingsService,
                archiveService,
                backupService,
                payrollService,
                localizationService,
                moduleRegistry,
                licensingService,
                licenseGate,
                trialService,
                accessController,
                updateService,
                attendanceService,
                leaveService,
                loanService,
                contractService,
                performanceService,
                assetService,
                trainingService,
                atsService,
                certificateService,
                dashboardService,
                reportService,
                notificationService,
                auditService,
                userService);
        }
    }
}
