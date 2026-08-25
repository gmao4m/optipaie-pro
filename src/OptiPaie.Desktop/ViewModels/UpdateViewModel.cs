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
        private string _fallbackUrl = string.Empty;

        public UpdateViewModel(AppServices services, AppUpdateCheck info)
        {
            _services = services;
            _info = info;
            UpdateCommand = new RelayCommand(async () => await UpdateAsync().ConfigureAwait(true), () => !_isBusy);
            LaterCommand = new RelayCommand(() => CloseRequested?.Invoke(false), () => !_isBusy && !_info.Mandatory);
            OpenInBrowserCommand = new RelayCommand(OpenInBrowser);
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

        /// <summary>Celebratory headline in the popup header ("La version X.X.X est prête…"), localized.</summary>
        public string Headline
        {
            get
            {
                string template = OptiPaie.Desktop.Localization.TranslationSource.Instance["Upd_Headline"];
                if (string.IsNullOrWhiteSpace(template) || template == "Upd_Headline" ||
                    template.IndexOf("{0}", StringComparison.Ordinal) < 0)
                {
                    template = "La version {0} est prête.";
                }

                try { return string.Format(template, _info.LatestVersion); }
                catch { return _info.AppName; }
            }
        }

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

        /// <summary>Direct installer URL to offer when the automatic update fails (manual fallback).</summary>
        public string FallbackUrl { get => _fallbackUrl; private set { if (Set(ref _fallbackUrl, value)) Raise(nameof(HasFallback)); } }
        public bool HasFallback => _failed && !string.IsNullOrWhiteSpace(_fallbackUrl);

        public ICommand UpdateCommand { get; }
        public ICommand LaterCommand { get; }
        public ICommand OpenInBrowserCommand { get; }

        private async Task UpdateAsync()
        {
            IsBusy = true;
            Failed = false;
            FallbackUrl = string.Empty;
            Progress = 0;
            StatusMessage = "جارٍ تنزيل التحديث…  Téléchargement de la mise à jour…";

            UpdateApplyResult result;
            try
            {
                var progress = new Progress<int>(p => Progress = p);
                result = await _services.Update
                    .DownloadAndApplyAsync(progress, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // Belt-and-suspenders: NEVER leave the button doing nothing on an unexpected error.
                _services.Logger.Error("Update apply threw.", ex);
                result = UpdateApplyResult.Fail(ex.Message, "https://github.com/gmao4m/optipaie-pro/releases/latest/download/OptiPaie-PRO-Setup.exe");
            }

            // On success the app relaunches into the new version (process exits); if we still get
            // here, surface the outcome — with a working manual fallback so the user is never stuck.
            if (result.Success)
            {
                StatusMessage = "جارٍ تثبيت التحديث…  Installation de la mise à jour…";
                return;
            }

            IsBusy = false;
            Failed = true;
            FallbackUrl = string.IsNullOrWhiteSpace(result.FallbackUrl)
                ? "https://github.com/gmao4m/optipaie-pro/releases/latest/download/OptiPaie-PRO-Setup.exe"
                : result.FallbackUrl;
            Raise(nameof(HasFallback));
            StatusMessage = "تعذّر التحديث التلقائي. اضغط « التنزيل عبر المتصفح » ثمّ ثبِّت الملف.\n" +
                            "Échec de la mise à jour automatique. Cliquez sur « Télécharger dans le navigateur » puis lancez le fichier.";
        }

        private void OpenInBrowser()
        {
            string url = string.IsNullOrWhiteSpace(_fallbackUrl)
                ? "https://github.com/gmao4m/optipaie-pro/releases/latest/download/OptiPaie-PRO-Setup.exe"
                : _fallbackUrl;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _services.Logger.Warn("Opening the update URL failed: " + ex.Message);
                StatusMessage = "افتح هذا الرابط يدويًا للتنزيل :\n" + url;
            }
        }
    }
}
