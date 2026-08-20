using System;
using System.Linq;
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
using OptiPaie.Services.Auth;

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
        private MainWindow _shell;
        private bool _timersStarted;
        private bool _companyChangeHooked;
        private bool _rebuildingShell;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Install last-resort crash capture FIRST, so any failure during startup,
            // composition, or later navigation is written to disk (see Common/CrashLog).
            CrashLog.Install(this);
            CrashLog.Breadcrumb("OnStartup");

            // CRITICAL: drive shutdown EXPLICITLY. With the WPF default (OnLastWindowClose) the
            // process dies the instant a login/activation dialog closes while it is the only open
            // window — before MainWindow exists — so a correct sign-in silently closed the app
            // (the v1.26 production crash). The main window's own close triggers Shutdown() via
            // Shell_Closed. See OptiPaie.Services.Auth.AppShellPolicy + LoginFlowTests.
            ShutdownMode = AppShellPolicy.ShutdownModeDuringGate == AppShutdownMode.OnExplicitShutdown
                ? ShutdownMode.OnExplicitShutdown
                : ShutdownMode.OnLastWindowClose;

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
                CrashLog.Breadcrumb("access/activation gate not passed at startup -> exit");
                Shutdown();
                return;
            }

            // Demo experience: on a trial with an empty database, fill it with a realistic
            // Algerian sample dataset so every screen demonstrates the product immediately.
            // Never runs for a licensed install or a database that already has data.
            SeedDemoDataIfTrial();

            // Single sign-in gate → single path to the main window. Cold start and post-signout
            // both converge here, and any exception is shown (not a silent process kill).
            try
            {
                if (AuthenticateIfNeeded())
                {
                    // Company gate: pick / create the active company BEFORE any data screen. A
                    // multi-company install never auto-opens one silently; nothing is loaded until
                    // a company is chosen. Closing the gate without a choice does not open the app.
                    if (!EnsureCompanySelected())
                    {
                        CrashLog.Breadcrumb("no company selected at startup -> exit");
                        Shutdown();
                        return;
                    }

                    HookCompanyChange();
                    ShowMainShell();
                }
                else
                {
                    CrashLog.Breadcrumb("sign-in cancelled at startup -> exit");
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Fatal("Startup sign-in flow", ex);
                MessageBox.Show(StartupErrorText(), "OptiPaie PRO", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        /// <summary>
        /// Builds and shows a FRESH main window — the single place the app is opened, used by
        /// both cold start and post-signout so the two paths cannot diverge. The main window's
        /// own close drives an explicit app shutdown (ShutdownMode is OnExplicitShutdown).
        /// </summary>
        private void ShowMainShell()
        {
            var window = new MainWindow();
            _shell = window;
            MainWindow = window;
            ApplyFlowDirection(window);
            window.Closed += Shell_Closed;
            window.Show();
            CrashLog.Breadcrumb("main shell shown");

            if (!_timersStarted)
            {
                _timersStarted = true;
                StartBackgroundLicenseSync();
                StartUpdateChecks();
                StartTrialWatchdog();
            }
        }

        private void Shell_Closed(object sender, EventArgs e)
        {
            // Only a genuine user close of the CURRENT shell exits the app. During signout the
            // handler is detached before the old shell is closed, so a rebuild never exits.
            if (ReferenceEquals(sender, _shell))
            {
                CrashLog.Breadcrumb("main window closed by user -> shutdown");
                Shutdown();
            }
        }

        private static string StartupErrorText()
        {
            return "حدث خطأ تقني. تم تسجيل التفاصيل في:\n" + CrashLog.Directory +
                   "\n\nUne erreur technique est survenue. Détails enregistrés dans le dossier ci-dessus.";
        }

        /// <summary>
        /// The blocking company gate, run BEFORE any data screen. Decides purely from how many
        /// companies exist (see <see cref="CompanySelectionPolicy"/>):
        /// <list type="bullet">
        /// <item>exactly one → enter it directly (no screen, no question, no click);</item>
        /// <item>several → a blocking picker (click a company to open it — no "validate" button);</item>
        /// <item>none → the same screen, offering to create the first company.</item>
        /// </list>
        /// Returns true only once a company is active. Nothing else is loaded until it does, so a
        /// data screen is never shown without an active company. Closing the gate without a choice
        /// returns false — the caller then exits rather than opening the app.
        /// </summary>
        private bool EnsureCompanySelected()
        {
            System.Collections.Generic.IReadOnlyList<Core.Entities.Company> companies = Services.Companies.GetAll();

            if (CompanySelectionPolicy.Decide(companies.Count) == CompanyStartupAction.EnterDirect)
            {
                // Exactly one company — open it directly, with no UI at all.
                Services.CompanyContext.Active = companies[0];
                return true;
            }

            // None (create the first) or several (choose) — a blocking screen the user cannot bypass.
            var viewModel = new CompanySelectionViewModel(Services);
            var window = new CompanySelectionWindow { DataContext = viewModel };
            ApplyFlowDirection(window);
            viewModel.CloseRequested = () => { try { window.DialogResult = true; } catch { window.Close(); } };
            window.ShowDialog();

            return Services.CompanyContext.ActiveId > 0;
        }

        /// <summary>Subscribes ONCE to header company switches so each one rebuilds the shell.</summary>
        private void HookCompanyChange()
        {
            if (_companyChangeHooked)
            {
                return;
            }

            _companyChangeHooked = true;
            Services.CompanyContext.ActiveChanged += OnHeaderCompanyChanged;
        }

        private void OnHeaderCompanyChanged(object sender, EventArgs e)
        {
            // Only a genuine header switch on a LIVE shell rebuilds. Company changes made before the
            // shell exists (the startup/signout gate) or during an in-progress rebuild are ignored.
            if (_shell == null || _rebuildingShell)
            {
                return;
            }

            _rebuildingShell = true; // coalesce re-entrancy; cleared when the rebuild completes
            // Defer: closing the shell from inside the header ComboBox's own selection event is
            // unsafe, so let that update finish first, then swap the shell.
            Dispatcher.BeginInvoke(new Action(RebuildShellForCompanyChange));
        }

        /// <summary>
        /// Changing the active company reloads EVERYTHING: every other open window is closed and a
        /// FRESH shell is built (new view models), so no screen keeps a byte of the previous
        /// company's data. There is no cache to leak between companies.
        /// </summary>
        private void RebuildShellForCompanyChange()
        {
            try
            {
                CrashLog.Breadcrumb("company changed -> full shell rebuild (no residual cache)");

                // Close every other open window so nothing keeps the previous company's data.
                foreach (Window w in Application.Current.Windows.OfType<Window>().ToList())
                {
                    if (!ReferenceEquals(w, _shell))
                    {
                        try { w.Close(); } catch { /* a window refusing to close must not block the rebuild */ }
                    }
                }

                // Tear down the current shell WITHOUT exiting the process (detach close->shutdown),
                // then build a brand-new one: new ShellViewModel, new module view models, no stale state.
                MainWindow oldShell = _shell;
                if (oldShell != null)
                {
                    oldShell.Closed -= Shell_Closed;
                    _shell = null;
                    oldShell.Close();
                }

                ShowMainShell();
            }
            catch (Exception ex)
            {
                CrashLog.Fatal("Company-change shell rebuild", ex);
                MessageBox.Show(StartupErrorText(), "OptiPaie PRO", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
            finally
            {
                _rebuildingShell = false;
            }
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
        /// Signs out and returns to the SINGLE sign-in screen — never closes the app. The
        /// license AND the local account are kept, so signing back in never re-asks for the
        /// license key. Converges with startup: on success a FRESH main window is built via
        /// the same <see cref="ShowMainShell"/> used at cold start.
        /// </summary>
        public void SignOut()
        {
            if (Services == null)
            {
                return;
            }

            CrashLog.Breadcrumb("SignOut");

            // End both principals but keep the license and the local account intact.
            Services.Session.Current = null;
            Services.CustomerAccount.SignOut();

            // Close the current shell WITHOUT exiting the process: detach the close->shutdown
            // handler first (ShutdownMode is OnExplicitShutdown, so no window = no auto-exit).
            MainWindow oldShell = _shell;
            if (oldShell != null)
            {
                oldShell.Closed -= Shell_Closed;
                _shell = null;
                oldShell.Close();
            }

            try
            {
                if (AuthenticateIfNeeded() && (Services.Access.Evaluate().CanUseApp || EnsureAccess()))
                {
                    // Re-run the company gate on a fresh sign-in: a different user must not inherit
                    // the previous session's company, and a multi-company install re-picks explicitly.
                    if (!EnsureCompanySelected())
                    {
                        CrashLog.Breadcrumb("no company selected after signout -> exit");
                        Shutdown();
                        return;
                    }

                    HookCompanyChange();
                    ShowMainShell();
                }
                else
                {
                    CrashLog.Breadcrumb("sign-in cancelled after signout -> exit");
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Fatal("SignOut sign-in flow", ex);
                MessageBox.Show(StartupErrorText(), "OptiPaie PRO", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        /// <summary>
        /// The single authentication gate. Shows the ONE unified login screen only when a
        /// sign-in is actually needed (owner account signed out, or the local login gate is on
        /// with no active session). Returns true once an authenticated principal exists — the
        /// owner account (email) OR a local user (username), auto-detected on the same screen.
        /// A remembered local user is restored silently first.
        /// </summary>
        private bool AuthenticateIfNeeded()
        {
            TryRestoreRememberedUser();

            ICustomerAccountService account = Services.CustomerAccount;
            bool ownerNeedsSignin = account != null && account.HasAccount && !account.IsSignedIn;
            bool localGateOn = Services.Users.IsLoginRequired();
            bool haveLocalSession = Services.Session != null && Services.Session.IsAuthenticated;

            if (!ownerNeedsSignin && (!localGateOn || haveLocalSession))
            {
                return true;
            }

            // Loop so the permanent « الدخول بمفتاح الترخيص » escape can bounce to the activation
            // screen and back without ever stranding the user. Every turn waits on a user action.
            while (true)
            {
                var viewModel = new LoginViewModel(Services);
                var window = new LoginWindow { DataContext = viewModel };
                ApplyFlowDirection(window);
                viewModel.CloseRequested = ok => { try { window.DialogResult = ok; } catch { window.Close(); } };
                window.ShowDialog();

                if (IsAuthenticated())
                {
                    return true;
                }

                if (viewModel.ActivationRequested)
                {
                    // Owner recovery: re-activating with the license key re-creates the account
                    // with a new password and signs it in — so a forgotten password is never a
                    // lock-out. If it worked, proceed; otherwise re-show the login screen.
                    ShowActivationScreen();
                    if (IsAuthenticated())
                    {
                        return true;
                    }
                    continue;
                }

                // The user closed the login without signing in and without asking for activation.
                return false;
            }
        }

        /// <summary>
        /// Shows the activation screen UNCONDITIONALLY (unlike <see cref="EnsureAccess"/>, which
        /// skips it when the license is already valid). Reached from the login screen's permanent
        /// license-key escape so an owner who forgot their password can re-activate and regain
        /// access even on an already-activated poste.
        /// </summary>
        private void ShowActivationScreen()
        {
            string knownEmail = Services.CustomerAccount != null && Services.CustomerAccount.HasAccount
                ? Services.CustomerAccount.Email
                : null;
            var viewModel = new ActivationViewModel(Services, ActivationMode.Activate, knownEmail);
            var window = new ActivationWindow { DataContext = viewModel };
            ApplyFlowDirection(window);
            viewModel.CloseRequested = ok => { try { window.DialogResult = ok; } catch { window.Close(); } };
            window.ShowDialog();
        }

        /// <summary>True when either principal is established: the owner session or a local user.</summary>
        private bool IsAuthenticated()
        {
            bool owner = Services.CustomerAccount != null && Services.CustomerAccount.IsSignedIn;
            bool local = Services.Session != null && Services.Session.IsAuthenticated;
            return owner || local;
        }

        /// <summary>
        /// "Remember me": silently restores a previously-remembered local user (still active)
        /// so the login screen is skipped. Never throws — any issue just falls through to login.
        /// </summary>
        private void TryRestoreRememberedUser()
        {
            try
            {
                if (Services.Session != null && Services.Session.IsAuthenticated) return;
                if (!Services.Users.IsLoginRequired()) return;

                string remembered = Services.Settings.Get(LoginViewModel.RememberedUserKey, null);
                if (string.IsNullOrWhiteSpace(remembered)) return;

                Core.Entities.User user = Services.Users.GetAll()
                    .FirstOrDefault(u => u.IsActive &&
                        string.Equals(u.Username, remembered, StringComparison.OrdinalIgnoreCase));
                if (user != null)
                {
                    Services.Session.Current = user;
                }
            }
            catch
            {
                // A failed auto-restore must never block the login screen.
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
