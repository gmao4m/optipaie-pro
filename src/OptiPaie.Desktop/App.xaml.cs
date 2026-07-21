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

            var window = new MainWindow();
            MainWindow = window;
            ApplyFlowDirection(window);
            window.Show();

            StartBackgroundLicenseSync();
            StartUpdateChecks();
            StartTrialWatchdog();
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
