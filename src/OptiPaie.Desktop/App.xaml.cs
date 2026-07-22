using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Updates;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Shell;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop
{
    /// <summary>
    /// Application entry point. Builds the service graph, applies the saved language,
    /// enforces the activation/trial gate, and opens the main window. The payroll
    /// engine and services are reused unchanged.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>The composed services, available to the whole UI.</summary>
        public static AppServices Services { get; private set; }

        private DispatcherTimer _licenseSyncTimer;
        private DispatcherTimer _updateTimer;
        private DispatcherTimer _trialWatchdog;
        private bool _updateDialogOpen;
        private bool _accessBlocked;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Velopack install/update hooks must run before anything else. In a normal
            // launch this returns immediately; during install/update Velopack handles
            // its hook arguments and exits. Never touches business data.
            Velopack.VelopackApp.Build().Run();

            base.OnStartup(e);

            // NOTE: this QuestPDF version predates the community-licence API
            // (Settings.License / LicenseType were introduced in QuestPDF 2023.4+),
            // so no runtime licence call is required — this build is MIT-licensed.

            try
            {
                Services = CompositionRoot.Build();

                string language = Services.Settings.GetLanguage();
                if (string.IsNullOrWhiteSpace(language))
                {
                    language = AppConstants.DefaultLanguage;
                }

                Services.Localization.SetLanguage(language);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erreur d'initialisation de l'application :\r\n\r\n" + ex.Message,
                    "OptiPaie PRO", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            // Activation gate: the app requires a valid license or an active trial.
            if (!EnsureAccess())
            {
                Shutdown();
                return;
            }

            // Demo experience: on a trial with an empty database, fill it with a realistic
            // Algerian sample dataset so every screen demonstrates the product immediately.
            // Never runs for a licensed install or a database that already has data.
            SeedDemoDataIfTrial();

            var window = new MainWindow();
            MainWindow = window;
            ApplyFlowDirection(window);
            window.Show();

            StartBackgroundLicenseSync();
            StartUpdateChecks();
            StartTrialWatchdog();
        }

        /// <summary>
        /// Seeds the demo dataset when the app is running on the trial AND the database is
        /// empty (a fresh demo install). A licensed install, or any database that already
        /// contains data, is left completely untouched. Failures never block startup.
        /// </summary>
        private void SeedDemoDataIfTrial()
        {
            try
            {
                if (Services.Access.Evaluate().State != AccessState.Trial)
                {
                    return;
                }

                var seeder = new OptiPaie.Services.DemoDataSeeder(
                    Services.Companies, Services.Employees, Services.Contracts, Services.Attendance,
                    Services.Leave, Services.Loans, Services.Assets, Services.Training,
                    Services.Certificates, Services.Performance);

                // Ensure the Algerian demo is present in trial mode: seeds on an empty DB, and
                // replaces any leftover (non-demo) data so the demo always shows.
                seeder.EnsureDemo();
            }
            catch
            {
                // Seeding demo data must never prevent the app from opening.
            }
        }

        /// <summary>
        /// Signs out of the current session: hides the main window and returns to the
        /// activation / trial-start screen. If access is still valid when that screen closes
        /// (e.g. the user continues the trial), the main window reopens; otherwise the app exits.
        /// </summary>
        public void SignOut()
        {
            if (Services == null)
            {
                return;
            }

            Window main = MainWindow;

            var viewModel = new ActivationViewModel(Services);
            var window = new ActivationWindow { DataContext = viewModel };
            ApplyFlowDirection(window);
            viewModel.CloseRequested = ok =>
            {
                try { window.DialogResult = ok; } catch { window.Close(); }
            };

            if (main != null)
            {
                main.Hide();
            }

            window.ShowDialog();

            if (Services.Access.Evaluate().CanUseApp)
            {
                if (main != null)
                {
                    main.Show();
                }
            }
            else
            {
                Shutdown();
            }
        }

        /// <summary>
        /// Enforces the 48-hour trial while the app runs: every 5 minutes it re-checks
        /// access, and the moment the trial lapses (with no license) it blocks the app,
        /// shows the activation window with the support contact, and closes if the user
        /// still cannot proceed. A licensed app is unaffected.
        /// </summary>
        private void StartTrialWatchdog()
        {
            _trialWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _trialWatchdog.Tick += (s, e) => EnforceAccessStillValid();
            _trialWatchdog.Start();
        }

        private void EnforceAccessStillValid()
        {
            if (_accessBlocked || Services == null)
            {
                return;
            }

            if (Services.Access.Evaluate().CanUseApp)
            {
                return;
            }

            _accessBlocked = true;
            _trialWatchdog?.Stop();

            // The trial lapsed mid-session — require activation before continuing.
            bool restored = EnsureAccess();
            if (restored)
            {
                _accessBlocked = false;
                _trialWatchdog?.Start();
                return;
            }

            MessageBox.Show(
                "Votre essai gratuit de 48 heures est terminé. Veuillez activer une licence pour continuer.",
                "OptiPaie PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
        }

        /// <summary>
        /// Checks for updates at startup and every 24 hours. Only active for a
        /// Velopack-installed build with a configured feed; otherwise a no-op.
        /// </summary>
        private void StartUpdateChecks()
        {
            if (Services.Update == null || !Services.Update.IsSupported)
            {
                return;
            }

            RunUpdateCheck();

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
            _updateTimer.Tick += (s, e) => RunUpdateCheck();
            _updateTimer.Start();
        }

        private async void RunUpdateCheck()
        {
            try
            {
                AppUpdateCheck check = await Services.Update.CheckForUpdatesAsync(CancellationToken.None);
                if (check.UpdateAvailable)
                {
                    ShowUpdateDialog(check);
                }
            }
            catch
            {
                // A background check must never crash the app.
            }
        }

        /// <summary>Shows the update dialog. A dismissed MANDATORY update closes the app.</summary>
        public void ShowUpdateDialog(AppUpdateCheck check)
        {
            if (_updateDialogOpen || MainWindow == null)
            {
                return;
            }

            _updateDialogOpen = true;
            bool proceed = false;

            var viewModel = new UpdateViewModel(Services, check);
            var window = new UpdateWindow { DataContext = viewModel, Owner = MainWindow };
            ApplyFlowDirection(window);
            viewModel.CloseRequested = ok =>
            {
                proceed = ok;
                try { window.DialogResult = ok; } catch { window.Close(); }
            };
            window.ShowDialog();

            _updateDialogOpen = false;

            // A mandatory update that was dismissed without installing blocks usage.
            if (check.Mandatory && !proceed)
            {
                MessageBox.Show(
                    "Une mise à jour obligatoire est disponible. Veuillez mettre à jour l'application avant de continuer.",
                    "OptiPaie PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
            }
        }

        /// <summary>
        /// Shows the activation window when the app cannot be used yet, and returns
        /// true once a valid license or an active trial is in place.
        /// </summary>
        private bool EnsureAccess()
        {
            if (Services.Access.Evaluate().CanUseApp)
            {
                return true;
            }

            var viewModel = new ActivationViewModel(Services);
            var activationWindow = new ActivationWindow { DataContext = viewModel };
            ApplyFlowDirection(activationWindow);
            viewModel.CloseRequested = ok => activationWindow.DialogResult = ok;
            activationWindow.ShowDialog();

            return Services.Access.Evaluate().CanUseApp;
        }

        /// <summary>
        /// Silent license validation: once at startup (picks up new modules / status)
        /// and every 24 hours while running. No-op when offline or not activated.
        /// </summary>
        private void StartBackgroundLicenseSync()
        {
            Task.Run(() => Services.Licensing.SynchronizeAsync(CancellationToken.None));

            _licenseSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
            _licenseSyncTimer.Tick += (s, e) =>
                Task.Run(() => Services.Licensing.SynchronizeAsync(CancellationToken.None));
            _licenseSyncTimer.Start();
        }

        /// <summary>Mirrors the whole UI for Arabic with a single flag — no per-control work.</summary>
        public static void ApplyFlowDirection(FrameworkElement root)
        {
            root.FlowDirection = Services != null && Services.Localization.IsRightToLeft
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }
    }
}
