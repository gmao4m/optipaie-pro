using System;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OptiPaie.Core.Licensing;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Services.Licensing;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>How the welcome window behaves.</summary>
    public enum ActivationMode
    {
        /// <summary>First use (or a new machine): create the account and activate a license key — or start the trial.</summary>
        Activate,

        /// <summary>Returning after a manual logout: email + password only. No license, no Internet.</summary>
        SignIn
    }

    /// <summary>
    /// The welcome / access window. Two modes:
    /// <list type="bullet">
    /// <item><b>Activate</b> — first use: enter email + password + company + license key.
    /// The <i>license</i> is the only thing that can block the app; the online account step
    /// is best-effort (for the owner's Admin panel) and never prevents a valid license from
    /// activating. On success the account is saved locally so the app auto-opens next time.</item>
    /// <item><b>SignIn</b> — after a manual logout: email + password only, verified against
    /// the local account. Fully offline; the license is never requested again.</item>
    /// </list>
    /// Validation errors are shown inline, under the field they concern; flow/server messages
    /// use the status banner.
    /// </summary>
    public sealed class ActivationViewModel : ObservableObject
    {
        private enum AccountStep { Proceed, Blocked }

        private readonly AppServices _services;
        private readonly SupabaseAuthClient _auth;
        private readonly TrialInfo _trial;

        private ActivationMode _mode;
        private string _email = string.Empty;
        private string _company = string.Empty;
        private string _licenseKey = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isError;
        private bool _isBusy;

        private string _emailError = string.Empty;
        private string _passwordError = string.Empty;
        private string _companyError = string.Empty;
        private string _keyError = string.Empty;

        public ActivationViewModel(AppServices services, ActivationMode mode = ActivationMode.Activate, string prefillEmail = null)
        {
            _services = services;
            _mode = mode;
            _trial = services.Trial.GetStatus();
            _email = prefillEmail ?? string.Empty;

            string baseUrl = Setting("Licensing.BaseUrl");
            _auth = new SupabaseAuthClient(SupabaseAuthClient.DeriveProjectUrl(baseUrl), Setting("Licensing.AnonKey"), services.Logger);

            TrialCommand = new RelayCommand(StartTrial, () => !_isBusy && CanStartTrial);
            UseLicenseCommand = new RelayCommand(() => Mode = ActivationMode.Activate, () => !_isBusy);
            CheckUpdatesCommand = new RelayCommand(CheckUpdates, () => !_isBusy);
        }

        /// <summary>Raised to close the window; true = the user may now use the app.</summary>
        public Action<bool> CloseRequested { get; set; }

        public string ProductName => "OptiPaie PRO";
        public string Subtitle => "Gestion de la paie & des RH";

        // ---- mode -----------------------------------------------------------
        public ActivationMode Mode
        {
            get => _mode;
            set
            {
                if (Set(ref _mode, value))
                {
                    Raise(nameof(IsActivateMode));
                    Raise(nameof(IsSignInMode));
                    Raise(nameof(ShowLicenseSection));
                    Raise(nameof(ShowCompanyField));
                    Raise(nameof(ShowSwitchToLicense));
                    Raise(nameof(PrimaryButtonText));
                    Raise(nameof(ModeHint));
                    ClearErrors();
                    SetStatus(string.Empty, false);
                }
            }
        }

        public bool IsActivateMode => _mode == ActivationMode.Activate;
        public bool IsSignInMode => _mode == ActivationMode.SignIn;

        /// <summary>License key + company + trial are shown only when activating.</summary>
        public bool ShowLicenseSection => IsActivateMode;
        public bool ShowCompanyField => IsActivateMode;

        /// <summary>The "use my license key" escape shown in sign-in mode (e.g. forgotten local password).</summary>
        public bool ShowSwitchToLicense => IsSignInMode;

        public string PrimaryButtonText => IsActivateMode ? "Créer mon compte et activer" : "Se connecter";

        public string ModeHint => IsActivateMode
            ? "Première utilisation : créez votre compte et activez votre clé de licence. Ce sera demandé une seule fois."
            : "Bon retour ! Connectez-vous avec votre email et votre mot de passe.";

        // ---- fields ---------------------------------------------------------
        public string Email
        {
            get => _email;
            set { if (Set(ref _email, value)) { EmailError = string.Empty; CommandManager.InvalidateRequerySuggested(); } }
        }

        public string Company
        {
            get => _company;
            set { if (Set(ref _company, value)) CompanyError = string.Empty; }
        }

        public string LicenseKey
        {
            get => _licenseKey;
            set
            {
                string formatted = LicenseKeyFormatter.Format(value);
                if (Set(ref _licenseKey, formatted))
                {
                    KeyError = string.Empty;
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string KeyHint => "Format : PAY-XXXX-XXXX-XXXX";

        // ---- inline (per-field) errors --------------------------------------
        public string EmailError { get => _emailError; private set => Set(ref _emailError, value); }
        public string PasswordError { get => _passwordError; private set => Set(ref _passwordError, value); }
        public string CompanyError { get => _companyError; private set => Set(ref _companyError, value); }
        public string KeyError { get => _keyError; private set => Set(ref _keyError, value); }

        /// <summary>Called from the code-behind when the password box changes, to clear its error.</summary>
        public void OnPasswordChanged() => PasswordError = string.Empty;

        // ---- status banner (flow + server messages) -------------------------
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public bool IsError { get => _isError; private set => Set(ref _isError, value); }
        public bool HasStatus => !string.IsNullOrEmpty(_statusMessage);

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    Raise(nameof(IsNotBusy));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsNotBusy => !_isBusy;

        // ---- trial (activate mode only) -------------------------------------
        public bool CanStartTrial => IsActivateMode && !_trial.IsExpired;
        public bool IsTrialExpired => _trial.IsExpired;

        public string TrialButtonText =>
            _trial.IsActive ? "Continuer l'essai" : "Démarrer l'essai gratuit — 48 h, tous les modules";

        public string TrialInfoText
        {
            get
            {
                if (_trial.IsActive) return "Essai en cours — " + _trial.RemainingText + " restant. Tous les modules débloqués.";
                if (_trial.IsExpired) return "Votre essai gratuit de 48 heures est terminé. Activez une licence pour continuer.";
                return "Ou évaluez OptiPaie PRO gratuitement pendant 48 heures, avec TOUS les modules.";
            }
        }

        public string SupportPhone => Setting("Support.Phone");
        public string SupportEmail => Setting("Support.Email");

        public string SupportText
        {
            get
            {
                string text = "Besoin d'aide ou d'une licence ? Contactez le support :" + Environment.NewLine + "Tél. : " + SupportPhone;
                if (!string.IsNullOrWhiteSpace(SupportEmail)) text += Environment.NewLine + "Email : " + SupportEmail;
                return text;
            }
        }

        public ICommand TrialCommand { get; }

        /// <summary>Switches sign-in mode back to full activation (recovery via the license key).</summary>
        public ICommand UseLicenseCommand { get; }

        /// <summary>« التحقق من التحديثات » — compares the running build to the published version.json.</summary>
        public ICommand CheckUpdatesCommand { get; }

        private string _updateMessage = string.Empty;

        /// <summary>Result of the update check, shown at the bottom of the login screen.</summary>
        public string UpdateMessage
        {
            get => _updateMessage;
            private set { if (Set(ref _updateMessage, value)) Raise(nameof(HasUpdateMessage)); }
        }

        public bool HasUpdateMessage => !string.IsNullOrEmpty(_updateMessage);

        /// <summary>
        /// Checks for a new version via the existing update service (version.json). Fully
        /// offline-safe: any failure shows a clear message and never crashes; the app stays
        /// usable offline.
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

                OptiPaie.Core.Updates.AppUpdateCheck check =
                    await _services.Update.CheckForUpdatesAsync(CancellationToken.None).ConfigureAwait(true);

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
            string repo = Setting("Update.GitHubRepo");
            return string.IsNullOrWhiteSpace(repo) ? string.Empty : "https://github.com/" + repo + "/releases/latest";
        }

        /// <summary>
        /// Runs the mode's action. Called from the window code-behind with the PasswordBox
        /// value (passwords are never bound or stored).
        /// </summary>
        public async Task SubmitAsync(string password)
        {
            if (_isBusy) return;

            ClearErrors();
            SetStatus(string.Empty, false);

            if (IsSignInMode)
            {
                SignInLocally(password);
                return;
            }

            await ActivateAsync(password).ConfigureAwait(true);
        }

        // -- sign-in mode: offline, against the local account -----------------
        private void SignInLocally(string password)
        {
            bool ok = true;
            if (string.IsNullOrWhiteSpace(_email)) { EmailError = "أدخل بريدك الإلكتروني. / Saisissez votre adresse email."; ok = false; }
            if (string.IsNullOrEmpty(password)) { PasswordError = "أدخل كلمة المرور. / Saisissez votre mot de passe."; ok = false; }
            if (!ok)
            {
                _services.Logger.Warn("Login blocked: required field empty (email='" + _email + "').");
                return;
            }

            IsBusy = true;
            try
            {
                string stored = _services.CustomerAccount.Email;
                if (!string.IsNullOrEmpty(stored) &&
                    !string.Equals(_email.Trim(), stored, StringComparison.OrdinalIgnoreCase))
                {
                    EmailError = "لا يوجد حساب بهذا البريد على هذا الجهاز. / Aucun compte trouvé pour cet email.";
                    _services.Logger.Warn("Login failed: unknown email '" + _email.Trim() + "' on this machine.");
                    return;
                }

                if (_services.CustomerAccount.SignIn(_email.Trim(), password))
                {
                    _services.Logger.Info("Login succeeded for '" + _email.Trim() + "'.");
                    CloseRequested?.Invoke(true);
                    return;
                }

                PasswordError = "بريد إلكتروني أو كلمة مرور خاطئة. / Email ou mot de passe incorrect.";
                _services.Logger.Warn("Login failed: wrong password for '" + _email.Trim() + "'.");
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Login failed unexpectedly for '" + _email + "'.", ex);
                SetStatus("حدث خطأ تقني غير متوقّع. / Une erreur technique inattendue est survenue.", true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // -- activate mode: license is the gate, account is best-effort -------
        private async Task ActivateAsync(string password)
        {
            if (!ValidateActivateInputs(password))
            {
                return;
            }

            IsBusy = true;
            try
            {
                // 1) Online account (for the owner's Admin panel). Blocks ONLY on issues the
                //    customer must fix here (offline, weak password, bad email, wrong password
                //    for an existing account). A pending email confirmation or a mail
                //    rate-limit NEVER blocks the license — that was the original activation bug.
                if (await EnsureOnlineAccountAsync(password).ConfigureAwait(true) == AccountStep.Blocked)
                {
                    return;
                }

                // 2) Activate the license — the real gate.
                SetStatus("Activation de la licence…", false);
                LicenseResult result = await _services.Licensing
                    .ActivateAsync(LicenseKeyFormatter.Canonical(_licenseKey), _company.Trim(), _email.Trim(), CancellationToken.None)
                    .ConfigureAwait(true);

                if (result.IsSuccess)
                {
                    CompleteActivation(password);
                    return;
                }

                // Re-activating the SAME poste that is already activated: a device_in_use /
                // device_mismatch from the server just means the license is already bound to
                // THIS machine — the legitimate poste must never be blocked by it.
                if ((result.Kind == LicenseResultKind.DeviceInUse || result.Kind == LicenseResultKind.DeviceMismatch)
                    && (_services.ActivationState.IsActivated || _services.Licensing.HasStoredLicense))
                {
                    _services.Logger.Info("Re-activation on the same already-activated device — accepted (" + result.Kind + ").");
                    CompleteActivation(password);
                    return;
                }

                ShowActivationError(result);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Activation flow failed unexpectedly.", ex);
                SetStatus("Une erreur inattendue est survenue.", true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Finalises a successful (or same-device) activation: records the "activated" marker
        /// so the poste is never re-asked for its license, saves the local account, and opens.
        /// </summary>
        private void CompleteActivation(string password)
        {
            _services.ActivationState.MarkActivated();

            try
            {
                _services.CustomerAccount.Register(_email.Trim(), _company.Trim(), password);
            }
            catch (Exception ex)
            {
                _services.Logger.Warn("Local account save failed (license is still active): " + ex.Message);
            }

            SetStatus("Licence activée avec succès. Bienvenue !", false);
            CloseRequested?.Invoke(true);
        }

        private bool ValidateActivateInputs(string password)
        {
            bool ok = true;

            if (string.IsNullOrWhiteSpace(_email)) { EmailError = "Saisissez votre adresse email."; ok = false; }
            else if (!LooksLikeEmail(_email)) { EmailError = "Adresse email invalide."; ok = false; }

            if (string.IsNullOrEmpty(password)) { PasswordError = "Saisissez votre mot de passe."; ok = false; }
            else if (password.Length < 6) { PasswordError = "Mot de passe trop court (au moins 6 caractères)."; ok = false; }

            if (string.IsNullOrWhiteSpace(_company)) { CompanyError = "Saisissez le nom de votre société."; ok = false; }

            if (!LicenseKeyFormatter.IsComplete(_licenseKey)) { KeyError = "Clé de licence incomplète (PAY-XXXX-XXXX-XXXX)."; ok = false; }

            return ok;
        }

        private async Task<AccountStep> EnsureOnlineAccountAsync(string password)
        {
            if (!_auth.IsConfigured)
            {
                return AccountStep.Proceed; // no backend configured — local-only account is enough
            }

            SetStatus("Création de votre compte…", false);
            AuthResult ar = await _auth.SignUpAsync(_email.Trim(), password, _company.Trim(), CancellationToken.None).ConfigureAwait(true);

            switch (ar.Kind)
            {
                case AuthErrorKind.None:
                    return AccountStep.Proceed;

                case AuthErrorKind.Offline:
                    // Do NOT block on the account step. The license activation below is the
                    // real connectivity test: if there is genuinely no network, ActivateAsync
                    // reports it; if the account call merely hiccuped, activation still works.
                    _services.Logger.Info("Online account step skipped (offline/transport); proceeding to license activation.");
                    return AccountStep.Proceed;

                case AuthErrorKind.WeakPassword:
                    PasswordError = "Mot de passe trop court (au moins 6 caractères).";
                    return AccountStep.Blocked;

                case AuthErrorKind.InvalidEmail:
                    EmailError = "Adresse email invalide.";
                    return AccountStep.Blocked;

                case AuthErrorKind.AlreadyExists:
                    // Same customer re-activating (new machine / reinstall) is normal —
                    // verify ownership by signing in with the password they just typed.
                    AuthResult si = await _auth.SignInAsync(_email.Trim(), password, CancellationToken.None).ConfigureAwait(true);
                    if (si.Success || si.Kind == AuthErrorKind.EmailNotConfirmed || si.Kind == AuthErrorKind.RateLimited)
                    {
                        return AccountStep.Proceed;
                    }
                    if (si.Kind == AuthErrorKind.InvalidCredentials)
                    {
                        PasswordError = "Cet email a déjà un compte. Mot de passe incorrect.";
                        return AccountStep.Blocked;
                    }
                    // Offline (or any other transport issue) here must NOT block: fall through
                    // to license activation, which is the authoritative connectivity check.
                    return AccountStep.Proceed;

                default:
                    // Email-confirmation pending, rate-limited, or any other soft issue must
                    // NOT block the license. Log and carry on to activation.
                    _services.Logger.Info("Online account step non-blocking (" + ar.Kind + "): " + ar.Message);
                    return AccountStep.Proceed;
            }
        }

        private void ShowActivationError(LicenseResult result)
        {
            // Every activation refusal is written to the persistent log (not only shown).
            _services.Logger.Warn("Activation refused: " + result.Kind + " — " + result.Message);

            switch (result.Kind)
            {
                case LicenseResultKind.InvalidKey:
                case LicenseResultKind.KeyInvalid:
                case LicenseResultKind.KeyUsed:
                case LicenseResultKind.KeyExpired:
                case LicenseResultKind.KeyRevoked:
                case LicenseResultKind.KeyWrongLicense:
                case LicenseResultKind.DeviceInUse:
                case LicenseResultKind.DeviceMismatch:
                case LicenseResultKind.WrongProduct:
                case LicenseResultKind.UnknownProduct:
                    // Key/device problems belong under the license-key field.
                    KeyError = "مفتاح الترخيص غير صالح أو غير مقبول. / " +
                        (string.IsNullOrWhiteSpace(result.Message) ? "Clé de licence invalide." : result.Message);
                    break;

                case LicenseResultKind.Offline:
                    SetStatus("لا يوجد اتصال بالإنترنت. التفعيل الأول يتطلّب اتصالاً. / Aucune connexion Internet — l'activation nécessite une connexion.", true);
                    break;

                default:
                    SetStatus("تعذّر تفعيل الترخيص. / " +
                        (string.IsNullOrWhiteSpace(result.Message) ? "L'activation a échoué." : result.Message), true);
                    break;
            }
        }

        private void StartTrial()
        {
            TrialInfo info = _services.Trial.StartTrial();
            if (info.IsActive) { CloseRequested?.Invoke(true); return; }
            SetStatus("La période d'essai est expirée. Veuillez activer une licence.", true);
        }

        private void ClearErrors()
        {
            EmailError = string.Empty;
            PasswordError = string.Empty;
            CompanyError = string.Empty;
            KeyError = string.Empty;
        }

        private void SetStatus(string message, bool isError)
        {
            IsError = isError;
            StatusMessage = message;
            Raise(nameof(HasStatus));
        }

        private static bool LooksLikeEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            int at = value.IndexOf('@');
            int dot = value.LastIndexOf('.');
            return at > 0 && dot > at + 1 && dot < value.Length - 1;
        }

        private static string Setting(string key)
        {
            try { return ConfigurationManager.AppSettings[key] ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
