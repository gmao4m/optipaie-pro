using System.IO;
using OptiPaie.Common.Configuration;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Interfaces;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Engine;
using OptiPaie.Data.Backup;
using OptiPaie.Data.Context;
using OptiPaie.Localization;
using OptiPaie.PayrollEngine;
using OptiPaie.Reporting.Engine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.App
{
    /// <summary>
    /// The single place where the object graph is assembled (manual dependency
    /// injection via constructor injection). Reads the configuration, initialises
    /// the database, and constructs every service, returning them as <see cref="AppServices"/>.
    /// </summary>
    public static class CompositionRoot
    {
        /// <summary>Builds and wires the application services.</summary>
        public static AppServices Build()
        {
            AppConfiguration configuration = AppConfigurationReader.Read();

            ILogger logger = new FileLogger(Path.Combine(configuration.DataDirectory, "Logs", AppConstants.LogFileName));

            // Database: ensure folders, register type handlers, run migrations.
            var bootstrapper = new DatabaseBootstrapper(configuration);
            SqliteConnectionFactory connectionFactory = bootstrapper.Initialize();

            IUnitOfWorkFactory unitOfWorkFactory = new UnitOfWorkFactory(connectionFactory);
            IDatabaseBackupProvider backupProvider = new SqliteBackupProvider(connectionFactory, configuration);

            // Services (constructor injection of repositories-factory, validators, infra).
            var companyService = new CompanyService(unitOfWorkFactory, new CompanyValidator());
            var employeeService = new EmployeeService(unitOfWorkFactory, new EmployeeValidator());
            var elementService = new PayrollElementService(unitOfWorkFactory, new PayrollElementValidator());
            var configurationService = new ConfigurationService(unitOfWorkFactory);
            var settingsService = new SettingsService(unitOfWorkFactory);
            var archiveService = new ArchiveService(unitOfWorkFactory);
            var backupService = new BackupService(backupProvider, unitOfWorkFactory, configuration, logger);
            var localizationService = new LocalizationService();

            // Payroll engine (pure) + orchestration service.
            IPayrollEngine engine = new PayrollCalculationEngine();
            var payrollService = new PayrollService(unitOfWorkFactory, configurationService, engine);

            var reportService = new ReportService(localizationService);
            var licenseService = new LicenseService(configuration, logger);

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
                reportService,
                licenseService,
                localizationService);
        }
    }
}
