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
    /// <summary>
    /// The welcome / access window: create a customer account or sign in, then activate a
    /// license key (all modules unlocked) — or start the free 48-hour trial. The account
    /// step (Supabase Auth) runs ONCE, at first activation; afterwards the app opens
    /// offline from its cached, encrypted license. The email + company entered here are
    /// stamped on the license so they appear in the owner's Admin panel.
    /// </summary>
    public sealed class ActivationViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly SupabaseAuthClient _auth;
        private readonly TrialInfo _trial;

        private bool _isSignUp = true;
        private string _email = string.Empty;
        private string _company = string.Empty;
        private string _licenseKey = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isError;
        private bool _isBusy;

        public ActivationViewModel(AppServices services)
        {
            _services = services;
            _trial = services.Trial.GetStatus();

            string baseUrl = Setting("Licensing.BaseUrl");
            _auth = new SupabaseAuthClient(SupabaseAuthClient.DeriveProjectUrl(baseUrl), Setting("Licensing.AnonKey"));

            SelectSignUpCommand = new RelayCommand(() => IsSignUp = true, () => !_isBusy);
            SelectSignInCommand = new RelayCommand(() => IsSignUp = false, () => !_isBusy);
            TrialCommand = new RelayCommand(StartTrial, () => !_isBusy && CanStartTrial);
        }

        /// <summary>Raised to close the window; true = the user may now use the app.</summary>
        public Action<bool> CloseRequested { get; set; }

        public string ProductName => "OptiPaie PRO";
        public string Subtitle => "Gestion de la paie & des RH";

        // ---- account mode ----
        public bool IsSignUp
        {
            get => _isSignUp;
            set
            {
                if (Set(ref _isSignUp, value))
                {
                    Raise(nameof(IsSignIn));
                    Raise(nameof(ShowCompanyField));
                    Raise(nameof(PrimaryButtonText));
                    Raise(nameof(ModeHint));
                    SetStatus(string.Empty, false);
                }
            }
        }

        public bool IsSignIn => !_isSignUp;
        public bool ShowCompanyField => _isSignUp;
        public string PrimaryButtonText => _isSignUp ? "Créer mon compte et activer" : "Se connecter et activer";
        public string ModeHint => _isSignUp
            ? "Nouveau client ? Créez votre compte, puis activez votre clé."
            : "Déjà client ? Connectez-vous, puis activez votre clé.";

        public string Email { get => _email; set { if (Set(ref _email, value)) CommandManager.InvalidateRequerySuggested(); } }
        public string Company { get => _company; set => Set(ref _company, value); }

        public string LicenseKey
        {
            get => _licenseKey;
            set
            {
                string formatted = LicenseKeyFormatter.Format(value);
                if (Set(ref _licenseKey, formatted))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string KeyHint => "Format : PAY-XXXX-XXXX-XXXX";

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

        // ---- trial ----
        public bool CanStartTrial => !_trial.IsExpired;
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

        public ICommand SelectSignUpCommand { get; }
        public ICommand SelectSignInCommand { get; }
        public ICommand TrialCommand { get; }

        /// <summary>
        /// Account (sign-up or sign-in) then license activation. Called from the window
        /// code-behind with the PasswordBox value (passwords are never bound or stored).
        /// </summary>
        public async Task SubmitAsync(string password)
        {
            if (_isBusy) return;

            if (!LicenseKeyFormatter.IsComplete(_licenseKey)) { SetStatus("Saisissez une clé de licence complète (PAY-XXXX-XXXX-XXXX).", true); return; }
            if (string.IsNullOrWhiteSpace(_email)) { SetStatus("Saisissez votre adresse email.", true); return; }
            if (string.IsNullOrEmpty(password)) { SetStatus("Saisissez votre mot de passe.", true); return; }
            if (_isSignUp && string.IsNullOrWhiteSpace(_company)) { SetStatus("Saisissez le nom de votre société.", true); return; }

            IsBusy = true;
            try
            {
                // 1) Account step (once, online). Skipped only if Auth isn't configured.
                if (_auth.IsConfigured)
                {
                    SetStatus(_isSignUp ? "Création de votre compte…" : "Connexion…", false);
                    AuthResult ar = _isSignUp
                        ? await _auth.SignUpAsync(_email.Trim(), password, _company.Trim(), CancellationToken.None).ConfigureAwait(true)
                        : await _auth.SignInAsync(_email.Trim(), password, CancellationToken.None).ConfigureAwait(true);

                    if (ar.IsOffline) { SetStatus("Aucune connexion Internet. La première activation nécessite une connexion.", true); return; }

                    // An existing account in sign-up mode is fine — carry on to activation.
                    if (!ar.Success && !(_isSignUp && ar.AlreadyExists)) { SetStatus(ar.Message, true); return; }
                }

                // 2) Activate the licence, stamping email + company for the Admin panel.
                SetStatus("Activation de la licence…", false);
                LicenseResult result = await _services.Licensing
                    .ActivateAsync(LicenseKeyFormatter.Canonical(_licenseKey), (_company ?? string.Empty).Trim(), (_email ?? string.Empty).Trim(), CancellationToken.None)
                    .ConfigureAwait(true);

                if (result.IsSuccess)
                {
                    SetStatus("Licence activée avec succès. Bienvenue !", false);
                    CloseRequested?.Invoke(true);
                    return;
                }

                SetStatus(result.Message, true);
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

        private void StartTrial()
        {
            TrialInfo info = _services.Trial.StartTrial();
            if (info.IsActive) { CloseRequested?.Invoke(true); return; }
            SetStatus("La période d'essai est expirée. Veuillez activer une licence.", true);
        }

        private void SetStatus(string message, bool isError)
        {
            IsError = isError;
            StatusMessage = message;
            Raise(nameof(HasStatus));
        }

        private static string Setting(string key)
        {
            try { return ConfigurationManager.AppSettings[key] ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
