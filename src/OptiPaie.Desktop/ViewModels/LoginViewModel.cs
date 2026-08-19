using System;
using System.Configuration;
using System.Threading;
using System.Windows.Input;
using OptiPaie.Core.Updates;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Services.Auth;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The ONE unified sign-in screen. The user types a single identifier — their owner
    /// email OR a local username — and <see cref="LoginCoordinator"/> auto-detects which it
    /// is. The owner email is always accepted as a rescue path, so the client can never be
    /// trapped behind the local-user login. Success establishes the matching session and
    /// asks the host to open the app; failure shows a precise Arabic reason and the process
    /// stays alive.
    /// </summary>
    public sealed class LoginViewModel : ObservableObject
    {
        /// <summary>Settings key: the local username to auto-restore next launch ("remember me").</summary>
        public const string RememberedUserKey = "Auth.RememberedUser";

        private readonly AppServices _services;
        private readonly LoginCoordinator _coordinator;

        private string _identifier = string.Empty;
        private string _statusMessage = string.Empty;
        private string _updateMessage = string.Empty;
        private bool _isError;
        private bool _rememberMe = true;

        public LoginViewModel(AppServices services)
        {
            _services = services;
            _coordinator = new LoginCoordinator(services.CustomerAccount, services.Users);
            LoginCommand = new RelayCommand(Login);
            ForgotPasswordCommand = new RelayCommand(ShowForgotPasswordHelp);
            UseLicenseCommand = new RelayCommand(RequestActivation);
            CheckUpdatesCommand = new RelayCommand(CheckUpdates);

            // Pre-fill the last-used identifier for convenience (owner email or remembered user).
            string remembered = _services.Settings.Get(RememberedUserKey, null);
            if (!string.IsNullOrWhiteSpace(remembered)) _identifier = remembered;
            else if (services.CustomerAccount != null && services.CustomerAccount.HasAccount) _identifier = services.CustomerAccount.Email;
        }

        /// <summary>Raised to close the window; true = signed in.</summary>
        public Action<bool> CloseRequested { get; set; }

        /// <summary>Supplied by the window to read the PasswordBox securely (never bound/stored).</summary>
        public Func<string> PasswordAccessor { get; set; }

        /// <summary>
        /// True when the user chose the « الدخول بمفتاح الترخيص » escape: the host should open the
        /// activation screen so an owner who forgot their password can re-activate and regain access.
        /// </summary>
        public bool ActivationRequested { get; private set; }

        public string ProductName => "OptiPaie PRO";

        public string Identifier { get => _identifier; set => Set(ref _identifier, value); }
        public bool RememberMe { get => _rememberMe; set => Set(ref _rememberMe, value); }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public bool IsError { get => _isError; private set => Set(ref _isError, value); }

        public string UpdateMessage { get => _updateMessage; private set { if (Set(ref _updateMessage, value)) Raise(nameof(HasUpdateMessage)); } }
        public bool HasUpdateMessage => !string.IsNullOrEmpty(_updateMessage);

        /// <summary>Permanent affordances — always visible, never gated on a failed attempt.</summary>
        public bool ShowLicenseEscape => LoginScreenAffordances.LicenseKeyEscapeAlwaysVisible;
        public bool ShowUpdateCheck => LoginScreenAffordances.UpdateCheckAlwaysVisible;

        public ICommand LoginCommand { get; }
        public ICommand ForgotPasswordCommand { get; }

        /// <summary>« الدخول بمفتاح الترخيص » — the always-visible escape to the activation screen.</summary>
        public ICommand UseLicenseCommand { get; }

        /// <summary>« التحقق من التحديثات » — informational update check, offline-safe.</summary>
        public ICommand CheckUpdatesCommand { get; }

        private void Login()
        {
            string password = PasswordAccessor != null ? PasswordAccessor() : string.Empty;

            LoginOutcome outcome = _coordinator.Login(_identifier, password);
            if (!outcome.Success)
            {
                IsError = true;
                StatusMessage = L(MessageKey(outcome.Failure));
                if (outcome.Failure == LoginFailure.Technical)
                {
                    _services.Logger.Warn("Login failed with a technical error for identifier '" + (_identifier ?? string.Empty).Trim() + "'.");
                }
                return;
            }

            // Seat the session for a local user (the owner session is opened inside the coordinator).
            if (outcome.Principal == LoginPrincipal.LocalUser)
            {
                _services.Session.Current = outcome.User;
            }

            // "Remember me": persist the local username for a silent auto-restore next launch.
            // The owner account persists on its own; clear the remembered local user for it.
            string toRemember = (RememberMe && outcome.Principal == LoginPrincipal.LocalUser && outcome.User != null)
                ? outcome.User.Username
                : string.Empty;
            try { _services.Settings.Set(RememberedUserKey, toRemember); } catch { /* non-fatal */ }

            _services.Logger.Info("Sign-in succeeded (" + outcome.Principal + ").");
            CloseRequested?.Invoke(true);
        }

        private void ShowForgotPasswordHelp()
        {
            Dialogs.Info(L("Login_ForgotHelp"));
        }

        /// <summary>
        /// The always-visible escape: close the login screen and ask the host to open the
        /// activation screen. Lets an owner who forgot their password re-activate with the
        /// license key and take back control — they are never locked out.
        /// </summary>
        private void RequestActivation()
        {
            ActivationRequested = true;
            CloseRequested?.Invoke(false);
        }

        /// <summary>
        /// Checks for a newer version via the existing update service (version.json). Fully
        /// offline-safe: any failure shows a clear message and never crashes. Informational —
        /// the login screen is where a blocked client needs this to unblock themselves.
        /// </summary>
        private async void CheckUpdates()
        {
            UpdateMessage = "جارٍ التحقق من التحديثات…";
            try
            {
                if (_services.Update == null || !_services.Update.IsSupported)
                {
                    UpdateMessage = "التحديث التلقائي يعمل بعد التثبيت عبر المُثبِّت الرسمي.";
                    return;
                }

                AppUpdateCheck check = await _services.Update.CheckForUpdatesAsync(CancellationToken.None).ConfigureAwait(true);
                if (check.UpdateAvailable)
                {
                    string url = ReleasesUrl();
                    UpdateMessage = "يتوفر تحديث جديد: " + check.LatestVersion +
                        (string.IsNullOrEmpty(url) ? string.Empty : "  —  " + url);
                }
                else
                {
                    UpdateMessage = "نسختك محدّثة (" + check.CurrentVersion + ").";
                }
            }
            catch (Exception ex)
            {
                _services.Logger.Warn("Update check (login screen) failed: " + ex.Message);
                UpdateMessage = "تعذّر التحقق من التحديثات — تحقّق من اتصالك بالإنترنت.";
            }
        }

        private static string ReleasesUrl()
        {
            string repo = ConfigurationManager.AppSettings["Update.GitHubRepo"];
            return string.IsNullOrWhiteSpace(repo) ? string.Empty : "https://github.com/" + repo + "/releases/latest";
        }

        private static string MessageKey(LoginFailure failure)
        {
            switch (failure)
            {
                case LoginFailure.MissingIdentifier: return "Login_Err_NeedUsername";
                case LoginFailure.MissingPassword: return "Login_Err_NeedPassword";
                case LoginFailure.Disabled: return "Login_Err_Disabled";
                case LoginFailure.Technical: return "Login_Err_Technical";
                default: return "Login_Err_BadCredentials";
            }
        }

        private string L(string key) => _services.Localization.GetString(key);
    }
}
