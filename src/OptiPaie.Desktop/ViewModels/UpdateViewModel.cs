using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OptiPaie.Core.Updates;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The update dialog: shows current/latest version + release notes, and downloads &
    /// applies the update with progress. For a mandatory update the "Later" button is
    /// hidden and dismissing the window blocks the app (handled by the caller).
    /// </summary>
    public sealed class UpdateViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly AppUpdateCheck _info;

        private int _progress;
        private bool _isBusy;
        private bool _failed;
        private string _statusMessage = string.Empty;

        public UpdateViewModel(AppServices services, AppUpdateCheck info)
        {
            _services = services;
            _info = info;
            UpdateCommand = new RelayCommand(async () => await UpdateAsync().ConfigureAwait(true), () => !_isBusy);
            LaterCommand = new RelayCommand(() => CloseRequested?.Invoke(false), () => !_isBusy && !_info.Mandatory);
        }

        /// <summary>true = updating/restarting; false = postponed.</summary>
        public Action<bool> CloseRequested { get; set; }

        public string AppName => _info.AppName;
        public string CurrentVersion => _info.CurrentVersion;
        public string LatestVersion => _info.LatestVersion;
        public bool Mandatory => _info.Mandatory;
        public bool CanPostpone => !_info.Mandatory;

        public string ReleaseNotes => string.IsNullOrWhiteSpace(_info.ReleaseNotes)
            ? "• Améliorations et corrections de bugs"
            : _info.ReleaseNotes;

        public string MandatoryText => _info.Mandatory
            ? "Cette mise à jour est obligatoire. Veuillez l'installer pour continuer à utiliser l'application."
            : string.Empty;

        public int Progress { get => _progress; private set => Set(ref _progress, value); }

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
        public bool Failed { get => _failed; private set => Set(ref _failed, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand UpdateCommand { get; }
        public ICommand LaterCommand { get; }

        private async Task UpdateAsync()
        {
            IsBusy = true;
            Failed = false;
            Progress = 0;
            StatusMessage = "Téléchargement de la mise à jour…";

            var progress = new Progress<int>(p => Progress = p);
            UpdateApplyResult result = await _services.Update
                .DownloadAndApplyAsync(progress, CancellationToken.None)
                .ConfigureAwait(true);

            // On success the app relaunches into the new version (process exits); if we
            // still get here, surface the outcome.
            if (result.Success)
            {
                StatusMessage = "Installation de la mise à jour…";
                return;
            }

            IsBusy = false;
            Failed = true;
            StatusMessage = result.Error;
        }
    }
}
