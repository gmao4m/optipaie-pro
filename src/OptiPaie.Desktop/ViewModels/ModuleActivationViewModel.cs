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
    /// Settings → Activate Module. The user pastes a single-use module activation key;
    /// it is validated online, the module is unlocked, and the encrypted cache updated.
    /// The navigation refreshes automatically (via the licensing Changed event), so no
    /// restart is needed.
    /// </summary>
    public sealed class ModuleActivationViewModel : ObservableObject
    {
        private readonly AppServices _services;

        private string _key = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isError;
        private bool _isBusy;
        private bool _done;

        public ModuleActivationViewModel(AppServices services)
        {
            _services = services;
            ActivateCommand = new RelayCommand(
                async () => await RunAsync().ConfigureAwait(true),
                () => !_isBusy && (_done || !string.IsNullOrWhiteSpace(_key)));
        }

        /// <summary>Raised to close; true when a module was activated (refresh the UI).</summary>
        public Action<bool> CloseRequested { get; set; }

        public string Key
        {
            get => _key;
            set
            {
                if (Set(ref _key, (value ?? string.Empty).ToUpperInvariant()))
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

        public bool ActivatedOk => _done;

        public string ButtonText => _done ? "Fermer" : "Activer le module";

        public ICommand ActivateCommand { get; }

        private async Task RunAsync()
        {
            if (_done)
            {
                CloseRequested?.Invoke(true);
                return;
            }

            IsBusy = true;
            SetStatus("Validation en cours…", false);

            LicenseResult result;
            try
            {
                result = await _services.Licensing
                    .ActivateModuleAsync(_key, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Module activation failed unexpectedly.", ex);
                IsBusy = false;
                SetStatus("Une erreur inattendue est survenue.", true);
                return;
            }

            IsBusy = false;

            if (result.IsSuccess)
            {
                _done = true;
                Raise(nameof(ButtonText));
                Raise(nameof(ActivatedOk));
                SetStatus("Module activé avec succès. Il est disponible immédiatement.", false);
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            SetStatus(result.Message, true);
        }

        private void SetStatus(string message, bool isError)
        {
            IsError = isError;
            StatusMessage = message;
        }
    }
}
