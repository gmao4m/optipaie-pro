using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OptiPaie.Core.Licensing;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The activation window: enter a license key to activate, or start / continue the
    /// 30-day trial. The key box auto-formats to XXXXX-XXXXX-XXXXX-XXXXX and Activate is
    /// only enabled once a complete key is present.
    /// </summary>
    public sealed class ActivationViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly TrialInfo _trial;

        private string _licenseKey = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isError;
        private bool _isBusy;

        public ActivationViewModel(AppServices services)
        {
            _services = services;
            _trial = services.Trial.GetStatus();

            ActivateCommand = new RelayCommand(
                async () => await ActivateAsync().ConfigureAwait(true),
                () => !_isBusy && LicenseKeyFormatter.IsComplete(_licenseKey));

            TrialCommand = new RelayCommand(StartTrial, () => !_isBusy && CanStartTrial);
        }

        /// <summary>Raised to close the window; true = the user may now use the app.</summary>
        public Action<bool> CloseRequested { get; set; }

        public string ProductName => "OptiPaie PRO";
        public string Subtitle => "Activation du logiciel";

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

        public string StatusMessage
        {
            get => _statusMessage;
            private set => Set(ref _statusMessage, value);
        }

        public bool IsError
        {
            get => _isError;
            private set => Set(ref _isError, value);
        }

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

        /// <summary>True unless the trial was started and has already expired.</summary>
        public bool CanStartTrial => !_trial.IsExpired;

        public string TrialButtonText =>
            _trial.IsActive ? "Continuer l'essai" : "Démarrer l'essai gratuit (30 jours)";

        public string TrialInfoText
        {
            get
            {
                if (_trial.IsActive)
                {
                    return "Essai en cours — " + _trial.DaysRemaining + " jour(s) restant(s).";
                }

                if (_trial.IsExpired)
                {
                    return "Votre période d'essai est terminée. Veuillez activer une licence pour continuer.";
                }

                return "Vous pouvez évaluer OptiPaie PRO gratuitement pendant 30 jours.";
            }
        }

        public ICommand ActivateCommand { get; }
        public ICommand TrialCommand { get; }

        private async Task ActivateAsync()
        {
            IsBusy = true;
            SetStatus("Activation en cours…", false);

            LicenseResult result;
            try
            {
                result = await _services.Licensing
                    .ActivateAsync(LicenseKeyFormatter.Canonical(_licenseKey), string.Empty, string.Empty, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Activation failed unexpectedly.", ex);
                IsBusy = false;
                SetStatus("Une erreur inattendue est survenue.", true);
                return;
            }

            IsBusy = false;

            if (result.IsSuccess)
            {
                SetStatus("Licence activée avec succès.", false);
                CloseRequested?.Invoke(true);
                return;
            }

            SetStatus(result.Message, true);
        }

        private void StartTrial()
        {
            TrialInfo info = _services.Trial.StartTrial();
            if (info.IsActive)
            {
                CloseRequested?.Invoke(true);
                return;
            }

            SetStatus("La période d'essai est expirée. Veuillez activer une licence.", true);
        }

        private void SetStatus(string message, bool isError)
        {
            IsError = isError;
            StatusMessage = message;
        }
    }
}
