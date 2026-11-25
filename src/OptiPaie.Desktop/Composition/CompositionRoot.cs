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
            return Build(AppConfigurationReader.Read());
        }

        /// <summary>
        /// Builds the full object graph against an explicit configuration. Production uses
        /// <see cref="Build()"/> (machine config); this overload lets a test compose the real graph
        /// over a temporary data directory — e.g. the startup render smoke test constructing the shell.
        /// Behaviour for production is unchanged (same body, config simply injected).
        /// </summary>
        public static AppServices Build(AppConfiguration configuration)
        {
            // Refuse to run from a shared/network location BEFORE touching the database: an SQLite
            // file opened from two machines over a share is silently corrupted. Throws a bilingual,
            // non-technical DataPathException that startup shows as-is. (A local path — the default
            // %AppData% — passes untouched, so no existing customer is affected.)
            DataPathGuard.Validate(configuration.DataDirectory);

            ILogger logger = new FileLogger(Path.Combine(configuration.DataDirectory, "Logs", AppConstants.LogFileName));

            var bootstrapper = new DatabaseBootstrapper(configuration);
            SqliteConnectionFactory connectionFactory = bootstrapper.Initialize();

            IUnitOfWorkFactory unitOfWorkFactory = new UnitOfWorkFactory(connectionFactory);
            IDatabaseBackupProvider backupProvider = new SqliteBackupProvider(connectionFactory, configuration);

            // Service-layer authorization choke point. Created now (empty), passed into every
            // write-capable service, and Bind-ed to the real session once it exists (below). Until
            // then — and whenever the login gate is off — it reports "not enforced" = full access,
            // so existing installs behave exactly as before.
            var access = new SessionAccessContext();

            var companyService = new CompanyService(unitOfWorkFactory, new CompanyValidator(), access);
            var employeeService = new EmployeeService(unitOfWorkFactory, new EmployeeValidator(), access);
            var elementService = new PayrollElementService(unitOfWorkFactory, new PayrollElementValidator(), access);
            var configurationService = new ConfigurationService(unitOfWorkFactory, access); // IRG/CNAS switch: admin-only write
            var settingsService = new SettingsService(unitOfWorkFactory);
            var archiveService = new ArchiveService(unitOfWorkFactory, access);
            var attendanceService = new AttendanceService(unitOfWorkFactory, access);
            var leaveService = new LeaveService(unitOfWorkFactory, access);
            var loanService = new LoanService(unitOfWorkFactory, access);
            var contractService = new ContractService(unitOfWorkFactory, access);
            var performanceService = new PerformanceService(unitOfWorkFactory, access);
            var assetService = new AssetService(unitOfWorkFactory, access);
            var departmentService = new DepartmentService(unitOfWorkFactory, access);
            var trainingService = new TrainingService(unitOfWorkFactory, access);
            var atsService = new AtsService(unitOfWorkFactory, new EmployeeValidator(), access);
            var certificateService = new WorkCertificateService(unitOfWorkFactory, access);
            var dashboardService = new DashboardService(
                companyService, employeeService, contractService, leaveService, loanService,
                attendanceService, atsService, assetService, trainingService, access);
            var reportService = new ReportService(
                companyService, employeeService, attendanceService, leaveService, loanService,
                atsService, assetService, trainingService, access);
            var notificationService = new NotificationService(
                companyService, employeeService, contractService, leaveService, trainingService, performanceService);

            // CNAS declarations (DAC/DAS) — read-only over persisted payroll + identity data.
            var cnasDeclarationService = new CnasDeclarationService(
                companyService, employeeService, archiveService, configurationService, access);

            // ATS/DRT official attestations — auto-fill the CNAS .docx templates from employee files.
            var atsDrtDocumentService = new AtsDrtDocumentService(companyService, employeeService, archiveService);

            // Audit trail. Wired into the lifecycle events of the modules through the
            // optional sink, so history is recorded without changing any service ctor.
            var auditService = new AuditService(unitOfWorkFactory, logger);
            leaveService.Audit = auditService;
            configurationService.Audit = auditService; // IRG/CNAS switch changes: who, when, old → new
            contractService.Audit = auditService;
            loanService.Audit = auditService;
            assetService.Audit = auditService;
            employeeService.Audit = auditService;   // employee lifecycle + salary changes
            atsService.Audit = auditService;         // recruitment: candidate hired -> employee
            var backupService = new BackupService(backupProvider, unitOfWorkFactory, configuration, logger, access);
            var localizationService = new LocalizationService();
            var userService = new UserService(unitOfWorkFactory, settingsService, access);
            userService.Audit = auditService;        // user management + login-gate changes

            IPayrollEngine engine = new PayrollCalculationEngine();
            // PART 2 — the pre-issue sanity guard, with the audit sink so an acknowledged
            // suspicious payslip records WHO issued it. Sanity service defaults inside.
            var payrollService = new PayrollService(
                unitOfWorkFactory, configurationService, engine, access, new PayslipSanityService(), auditService);

            // Payslip correction / deletion / restore. Re-runs the SAME engine (via Preview) and
            // persists the result; the payslip status change and the loan reversal/re-application
            // commit in one transaction. Audit + actor are wired below with the other sinks.
            var payslipRevisionService = new PayslipRevisionService(
                unitOfWorkFactory, payrollService, configurationService, loanService, access);
            payslipRevisionService.Audit = auditService; // payslip correction / cancellation / restore

            // Employee import/export (xlsx/csv, atomic bulk import). Shares the same EmployeeValidator
            // as manual entry so the import enforces identical rules, nothing stricter.
            var employeeImportExport = new OptiPaie.Services.ImportExport.EmployeeImportExportService(
                unitOfWorkFactory, new EmployeeValidator(), access);
            employeeImportExport.Audit = auditService;

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

            // The single local customer account (login/logout after first activation).
            // Stored in the settings key/value table and DPAPI-wrapped — no migration,
            // no network: sign-in after a logout is fully offline and never re-asks for a key.
            var customerAccountImpl = new CustomerAccountService(settingsService, cipher, logger);
            customerAccountImpl.Audit = auditService; // owner-recovery attempts are audited
            ICustomerAccountService customerAccount = customerAccountImpl;

            // "Activated once" marker (plain settings flag). Lets the startup / reconnection
            // gate trust a prior activation and never re-ask a paying customer for the license.
            IActivationState activationState = new ActivationState(settingsService);

            // Auto-update. The release channel is injected; the service + metadata source are
            // provider-agnostic and unit-tested. Disabled gracefully (IsSupported=false) on
            // non-installed/dev runs or when unconfigured.
            UpdateOptions updateOptions = UpdateConfig.Build(licensingOptions);
            // Update-source precedence — both IReleaseChannel, so the pop-up, orchestration and
            // offline handling are identical: (1) a self-hosted version.json (simplest: one file
            // carrying the version + installer URL + mandatory flag), else (2) GitHub Releases.
            // Any dev/unconfigured run reports IsSupported=false (no-op).
            //
            // The old Velopack feed rail was REMOVED (2026-08-17): its Setup.exe stub statically
            // imports GetDpiForSystem and cannot launch on Windows 7 SP1. Do not reintroduce it.
            IReleaseChannel releaseChannel;
            IUpdateMetadataSource updateMetadata;
            if (!string.IsNullOrWhiteSpace(updateOptions.VersionJsonUrl))
            {
                var versionJson = new VersionJsonReleaseChannel(updateOptions, logger);
                releaseChannel = versionJson;
                updateMetadata = versionJson; // mandatory flag + notes come from the same manifest
            }
            else
            {
                releaseChannel = new GitHubReleaseChannel(updateOptions, logger);
                updateMetadata = new SupabaseUpdateMetadataSource(updateOptions, logger);
            }
            IUpdateService updateService = new UpdateService(releaseChannel, updateMetadata, updateOptions, logger);

            // PART 3 — read-only exposure diagnostic (exempt allowances / unenforced ceilings).
            var payrollExposureService = new PayrollExposureService(unitOfWorkFactory);

            // Commit 2 — per-company work calendar (weekly pattern, holidays reuse, proration basis).
            var holidayService = new HolidayService(unitOfWorkFactory, access);
            var workCalendarService = new WorkCalendarService(unitOfWorkFactory, holidayService, access);

            AppServices appServices = new AppServices(
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
                payslipRevisionService,
                employeeImportExport,
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
                departmentService,
                trainingService,
                atsService,
                certificateService,
                dashboardService,
                reportService,
                notificationService,
                auditService,
                userService,
                cnasDeclarationService,
                atsDrtDocumentService,
                customerAccount,
                activationState,
                payrollExposureService,
                workCalendarService);

            // Configurable-types + holidays services (post-construction, like leaveService.Audit above).
            appServices.LeaveTypes = new LeaveTypeService(unitOfWorkFactory, access);
            appServices.Holidays = holidayService;
            appServices.DemoData = new DemoDataService(unitOfWorkFactory, backupService, access);
            // Commit 4 — company-wide payroll + the pre-run review (shared by the single and batch screens).
            appServices.Batch = new BatchPayrollService(
                employeeService, elementService, loanService, attendanceService, payrollService,
                contractService, archiveService, licenseGate.IsEnabled, access, workCalendarService);

            // Bind the service-layer authorization choke point to the real session now that it exists.
            // Enforcement activates only while the login gate is on AND a user exists; the signed-in
            // role then drives AccessPolicy at every write-capable service.
            access.Bind(appServices.Session, userService, customerAccount);
            appServices.Authorization = access; // the UI gates navigation/actions from the same context

            // First-launch-after-upgrade owner setup: establishes the OWNER (adopt the existing
            // account in place, or create one) without ever touching the business data. Audited.
            var ownerSetup = new OptiPaie.Services.Startup.OwnerSetupService(
                settingsService, customerAccount, companyService, logger);
            ownerSetup.Audit = auditService;
            appServices.OwnerSetup = ownerSetup;
            appServices.DeviceIdentity = deviceIdentity; // recovery screen shows the install fingerprint

            // Attribute every audit entry to WHO performed it. Evaluated at record time so it
            // always reflects the CURRENT session: the signed-in local user (username), else the
            // owner account (email), else null → AuditService falls back to a generic actor.
            auditService.ActorProvider = () =>
            {
                UserSession session = appServices.Session;
                if (session != null && session.IsAuthenticated && session.Current != null &&
                    !string.IsNullOrWhiteSpace(session.Current.Username))
                {
                    return session.Current.Username;
                }

                OptiPaie.Core.Licensing.ICustomerAccountService owner = appServices.CustomerAccount;
                if (owner != null && owner.IsSignedIn && !string.IsNullOrWhiteSpace(owner.Email))
                {
                    return owner.Email;
                }

                return null;
            };

            // The payslip revision service records WHO corrected/cancelled on the payslip row itself.
            // Reuse the exact same actor resolution as the audit log, so the two always agree.
            payslipRevisionService.ActorProvider = auditService.ActorProvider;

            // Scope every audit entry to the ACTIVE company, resolved at record time. This is the
            // single choke point through which all module writes pass, so no call site can forget
            // to tag its entry — the per-company journal can never leak one client's actions into
            // another's. Null when no company is active yet (e.g. demo seeding before login): the
            // entry is stored with CompanyId NULL and stays out of every per-company journal.
            auditService.CompanyProvider = () =>
            {
                long id = appServices.CompanyContext.ActiveId;
                return id > 0 ? id : (long?)null;
            };

            return appServices;
        }
    }
}
