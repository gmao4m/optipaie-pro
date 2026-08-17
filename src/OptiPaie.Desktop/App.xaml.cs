using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Updates;
using OptiPaie.Desktop.Common;
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
            // Install last-resort crash capture FIRST, so any failure during startup,
            // composition, or later navigation is written to disk (see Common/CrashLog).
            CrashLog.Install(this);
            CrashLog.Breadcrumb("OnStartup");

            // Force modern TLS for every outbound HTTPS call (Supabase activation, GitHub
            // updates). .NET Framework can otherwise negotiate only TLS 1.0/1.1, which
            // Supabase/Cloudflare reject — the handshake then throws and was being reported
            // to the user as a bogus "Aucune connexion Internet" during activation.
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch { }
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls13; } catch { }

            base.OnStartup(e);

            // NOTE: this QuestPDF version predates the community-licence API
            // (Settings.License / LicenseType were introduced in QuestPDF 2023.4+),
            // so no runtime licence call is required — this build is MIT-licensed.

            // Register the bundled IBM Plex faces with QuestPDF so printed documents share
            // the on-screen type identity on any machine. Must run before any PDF is built.
            Documents.PdfFonts.Register();

            try
            {
                Services = CompositionRoot.Build();

                string language = Services.Settings.GetLanguage();
                if (string.IsNullOrWhiteSpace(language))
                {
                    language = AppConstants.DefaultLanguage;
                }

                Services.Localization.SetLanguage(language);

                // WPF binding StringFormat ignores the thread culture and formats via each
                // element's Language (default en-US), so amounts/dates showed US-style
                // ("60,000.00 DA"). Make elements inherit the app culture so numbers and
                // dates read the Algerian way ("60 000,00 DA", dd/MM/yyyy). Format only.
                try
                {
                    System.Windows.FrameworkElement.LanguageProperty.OverrideMetadata(
                        typeof(System.Windows.FrameworkElement),
                        new System.Windows.FrameworkPropertyMetadata(
                            System.Windows.Markup.XmlLanguage.GetLanguage(
                                Services.Localization.CurrentCulture.IetfLanguageTag)));
                }
                catch { /* already overridden — ignore */ }

                // Bridge the localization service to data binding so {loc:Loc ...} text
                // resolves for the active language and updates live on a language switch.
                Localization.TranslationSource.Instance.Attach(Services.Localization);

                // Apply the saved colour theme (light/dark) before any window is shown.
                bool darkTheme = string.Equals(Services.Settings.Get("Ui.Theme", "light"), "dark", StringComparison.OrdinalIgnoreCase);
                ThemeManager.Apply(darkTheme);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erreur d'initialisation de l'application :\r\n\r\n" + ex.Message,
                    "OptiPaie PRO", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            // Migration / backfill: a machine already activated under an OLDER version has a
            // stored license but no "activated" marker yet. Recognise it as activated so this
            // update never asks such a paying customer to re-enter their license.
            if (Services.Licensing.HasStoredLicense)
            {
                Services.ActivationState.MarkActivated();
            }

            // Activation gate: license checked only until the poste is activated; afterwards it
            // opens on the trusted marker and never re-asks for the license.
            if (!EnsureAccess())
            {
                Shutdown();
                return;
            }

            // Customer-session gate: after the first activation the customer stays signed in
            // automatically (no account, no license re-entry). Only a manual logout brings up
            // the sign-in screen, and then it asks for email + password ONLY.
            if (!EnsureCustomerSession())
            {
                Shutdown();
                return;
            }

            // Demo experience: on a trial with an empty database, fill it with a realistic
            // Algerian sample dataset so every screen demonstrates the product immediately.
            // Never runs for a licensed install or a database that already has data.
            SeedDemoDataIfTrial();

            // Optional login gate (dormant unless an admin enabled it and users exist).
            if (!EnsureLogin())
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
        /// Signs out of the current session and returns to the sign-in screen. The license
        /// AND the local account are kept, so the next sign-in needs only email + password —
        /// never the license key again. If no customer account exists (trial / legacy install)
        /// it falls back to the optional login gate or the activation screen.
        /// </summary>
        public void SignOut()
        {
            if (Services == null)
            {
                return;
            }

            // End sessions but keep the license and the local account intact.
            Services.Session.Current = null;              // dormant multi-user session (if any)
            Services.CustomerAccount.SignOut();

            Window main = MainWindow;
            if (main != null)
            {
                main.Hide();
            }

            bool proceed;
            if (Services.CustomerAccount.HasAccount)
            {
                // The normal path: email + password only (no license).
                proceed = PromptCustomerSignIn();

                // Mirror startup: if the optional multi-user login is also enabled, re-authenticate
                // the operator so their per-user session (and role) is re-established — never leave
                // Session.Current null, which would read as unrestricted admin.
                if (proceed && Services.Users.IsLoginRequired())
                {
                    proceed = EnsureLogin();
                }
            }
            else if (Services.Users.IsLoginRequired())
            {
                proceed = EnsureLogin();
            }
            else
            {
                // No customer account (trial / legacy) — show the activation / trial screen.
                proceed = EnsureAccess();
            }

            // After a successful sign-in the license normally still applies; if it lapsed
            // while signed out, offer activation before giving up.
            if (proceed && (Services.Access.Evaluate().CanUseApp || EnsureAccess()))
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
        /// Customer-session gate: auto-opens when the account is signed in (or when there is
        /// no account yet — a trial or a legacy licensed install); after a manual logout it
        /// asks for email + password ONLY (never the license again). Returns true to proceed.
        /// </summary>
        private bool EnsureCustomerSession()
        {
            OptiPaie.Core.Licensing.ICustomerAccountService account = Services.CustomerAccount;
            if (account == null || !account.HasAccount || account.IsSignedIn)
            {
                return true;
            }

            return PromptCustomerSignIn();
        }

        /// <summary>Shows the sign-in screen (email + password only) for the local account.</summary>
        private bool PromptCustomerSignIn()
        {
            var viewModel = new ActivationViewModel(Services, ActivationMode.SignIn, Services.CustomerAccount.Email);
            var window = new ActivationWindow { DataContext = viewModel };
            ApplyFlowDirection(window);
            viewModel.CloseRequested = ok => { try { window.DialogResult = ok; } catch { window.Close(); } };
            window.ShowDialog();
            return Services.CustomerAccount.IsSignedIn;
        }

        /// <summary>
        /// Shows the login screen when the gate is enabled and users exist; returns true once
        /// signed in. When login is not enforced, returns true immediately (full access).
        /// </summary>
        private bool EnsureLogin()
        {
            if (Services == null || !Services.Users.IsLoginRequired())
            {
                return true;
            }

            var viewModel = new ViewModels.LoginViewModel(Services);
            var window = new LoginWindow { DataContext = viewModel };
            ApplyFlowDirection(window);
            viewModel.CloseRequested = result => { try { window.DialogResult = result; } catch { window.Close(); } };

            bool ok = window.ShowDialog() == true;
            return ok && Services.Session.IsAuthenticated;
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

            // An already-activated poste is never hard-blocked mid-session by a local
            // re-verification issue; server-confirmed expiry / revocation are surfaced by the
            // background sync, not by tearing the app down at runtime.
            if (AccessDecision.MayOpen(Services.Access.Evaluate().CanUseApp, Services.ActivationState.IsActivated))
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
        /// Checks for updates at startup and every 24 hours. Only active when an
        /// update source (version.json / GitHub) is configured; otherwise a no-op.
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
            catch (Exception ex)
            {
                // A background check must never crash the app — but log it (not silently
                // swallow), so a real failure is visible in the breadcrumbs, not hidden.
                CrashLog.Breadcrumb("Update check skipped: " + ex.Message);
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

            // Defensive: a failure building/showing the dialog must never crash the app
            // (this is where a read-only-binding bug used to throw and be swallowed silently).
            try
            {
                var viewModel = new UpdateViewModel(Services, check);
                var window = new UpdateWindow { DataContext = viewModel, Owner = MainWindow };
                ApplyFlowDirection(window);
                viewModel.CloseRequested = ok =>
                {
                    proceed = ok;
                    try { window.DialogResult = ok; } catch { window.Close(); }
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                CrashLog.Breadcrumb("Update dialog failed: " + ex.Message);
            }

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
            bool canUse = Services.Access.Evaluate().CanUseApp;
            bool activated = Services.ActivationState.IsActivated;

            // Open WITHOUT prompting when usable now OR already activated (the trusted marker):
            // a transient LOCAL license-verification issue (device-id drift, unreadable cache)
            // must never re-ask an already-activated poste for its license. Server-confirmed
            // expiry / revocation are surfaced by the background sync, not blocked here.
            if (AccessDecision.MayOpen(canUse, activated))
            {
                if (canUse)
                {
                    Services.ActivationState.MarkActivated(); // confirm the marker while usable
                }

                return true;
            }

            // Genuinely never activated (and no active trial) → activation is required.
            // Pre-fill the email from the local account so a returning customer never retypes it.
            string knownEmail = Services.CustomerAccount != null && Services.CustomerAccount.HasAccount
                ? Services.CustomerAccount.Email
                : null;
            var viewModel = new ActivationViewModel(Services, ActivationMode.Activate, knownEmail);
            var activationWindow = new ActivationWindow { DataContext = viewModel };
            ApplyFlowDirection(activationWindow);
            viewModel.CloseRequested = ok => activationWindow.DialogResult = ok;
            activationWindow.ShowDialog();

            // Activation may now have made the app usable and/or set the marker.
            return AccessDecision.MayOpen(Services.Access.Evaluate().CanUseApp, Services.ActivationState.IsActivated);
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
