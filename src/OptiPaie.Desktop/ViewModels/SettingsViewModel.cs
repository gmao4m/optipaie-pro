using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Updates;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>Settings: language (live RTL switch), legal rate (read-only), backup/restore, license.</summary>
    public sealed class SettingsViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;
        private string _language;
        private string _cnasRateText = string.Empty;
        private string _versionText = string.Empty;

        private string _accessStateText = string.Empty;
        private string _customerText = string.Empty;
        private string _licenseTypeText = string.Empty;
        private string _productText = "OptiPaie PRO";
        private string _licenseKeyText = string.Empty;
        private string _expirationText = string.Empty;
        private string _activatedText = string.Empty;
        private string _deviceIdText = string.Empty;
        private string _modulesText = string.Empty;
        private string _lastUpdateCheckText = "Dernière vérification : jamais";

        public SettingsViewModel(AppServices services)
        {
            _services = services;

            Languages = new List<EnumOption>
            {
                new EnumOption("fr", "Français"),
                new EnumOption("ar", "العربية")
            };

            BackupCommand = new RelayCommand(Backup);
            RestoreCommand = new RelayCommand(Restore);
            RenewCommand = new RelayCommand(RenewLicense);
            DeactivateCommand = new RelayCommand(DeactivateDevice);
            CheckUpdatesCommand = new RelayCommand(CheckUpdates);
            ActivateModuleCommand = new RelayCommand(ActivateModule);
        }

        public List<EnumOption> Languages { get; }

        public string Language
        {
            get => _language;
            set { if (Set(ref _language, value)) ApplyLanguage(value); }
        }

        public string CnasRateText
        {
            get => _cnasRateText;
            private set => Set(ref _cnasRateText, value);
        }

        public string VersionText
        {
            get => _versionText;
            private set => Set(ref _versionText, value);
        }

        public ICommand BackupCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand RenewCommand { get; }
        public ICommand DeactivateCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand ActivateModuleCommand { get; }

        // -- License page bindings --------------------------------------------
        public string AccessStateText { get => _accessStateText; private set => Set(ref _accessStateText, value); }
        public string CustomerText { get => _customerText; private set => Set(ref _customerText, value); }
        public string LicenseTypeText { get => _licenseTypeText; private set => Set(ref _licenseTypeText, value); }
        public string ProductText { get => _productText; private set => Set(ref _productText, value); }
        public string LicenseKeyText { get => _licenseKeyText; private set => Set(ref _licenseKeyText, value); }
        public string ExpirationText { get => _expirationText; private set => Set(ref _expirationText, value); }
        public string ActivatedText { get => _activatedText; private set => Set(ref _activatedText, value); }
        public string DeviceIdText { get => _deviceIdText; private set => Set(ref _deviceIdText, value); }
        public string ModulesText { get => _modulesText; private set => Set(ref _modulesText, value); }
        public string LastUpdateCheckText { get => _lastUpdateCheckText; private set => Set(ref _lastUpdateCheckText, value); }

        public void OnActivated()
        {
            _language = _services.Localization.CurrentLanguage;
            Raise(nameof(Language));

            decimal rate = _services.ConfigurationService.GetCnasEmployeeRate();
            CnasRateText = (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %";

            System.Version version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText = "OptiPaie PRO  ·  version " + (version != null ? version.ToString(3) : "1.0.0");

            RefreshLicense();
        }

        private void RefreshLicense()
        {
            AccessEvaluation access = _services.Access.Evaluate();
            LicenseSnapshot lic = access.License;
            TrialInfo trial = access.Trial;
            bool licensed = lic.IsActivated;

            switch (access.State)
            {
                case AccessState.Licensed:
                    AccessStateText = "Sous licence — active";
                    break;
                case AccessState.Trial:
                    AccessStateText = "Essai gratuit (48 h) — " + trial.RemainingText + " restant · tous les modules";
                    break;
                case AccessState.TrialExpired:
                    AccessStateText = "Essai expiré";
                    break;
                case AccessState.Locked:
                    AccessStateText = "Licence non utilisable (" + lic.ServerStatus + ")";
                    break;
                default:
                    AccessStateText = "Non activé";
                    break;
            }

            CustomerText = licensed && !string.IsNullOrWhiteSpace(lic.CustomerName) ? lic.CustomerName : "—";
            LicenseTypeText = licensed
                ? LicenseTypes.DisplayName(lic.Type)
                : (trial.IsActive ? "Essai" : "—");
            LicenseKeyText = licensed ? Mask(lic.LicenseKey) : "—";
            ProductText = "OptiPaie PRO";

            if (licensed)
            {
                ExpirationText = lic.ExpiresAtUtc.HasValue
                    ? lic.ExpiresAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy")
                    : "Permanente";
                ActivatedText = lic.ActivatedAtUtc.HasValue
                    ? lic.ActivatedAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy")
                    : "—";
                ModulesText = lic.Modules.Count > 0 ? string.Join(", ", lic.Modules.OrderBy(m => m)) : "—";
            }
            else if (trial.IsActive)
            {
                ExpirationText = trial.ExpiresUtc.HasValue
                    ? trial.ExpiresUtc.Value.ToLocalTime().ToString("dd/MM/yyyy")
                    : "—";
                ActivatedText = trial.StartedUtc.HasValue
                    ? trial.StartedUtc.Value.ToLocalTime().ToString("dd/MM/yyyy")
                    : "—";
                ModulesText = "Produit de base (essai)";
            }
            else
            {
                ExpirationText = "—";
                ActivatedText = "—";
                ModulesText = "—";
            }

            DeviceIdText = _services.Licensing.DeviceId;

            DateTime? lastCheck = _services.Update != null ? _services.Update.LastCheckUtc : null;
            LastUpdateCheckText = "Dernière vérification : " +
                (lastCheck.HasValue ? lastCheck.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "jamais");
        }

        private static string Mask(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "—";
            }

            int dash = key.IndexOf('-');
            string head = dash > 0 ? key.Substring(0, dash) : key;
            return head + "-•••••-•••••-•••••";
        }

        private void RenewLicense()
        {
            var viewModel = new ActivationViewModel(_services);
            var window = new ActivationWindow
            {
                DataContext = viewModel,
                Owner = Application.Current.MainWindow
            };
            App.ApplyFlowDirection(window);
            viewModel.CloseRequested = ok => window.DialogResult = ok;
            window.ShowDialog();
            RefreshLicense();
        }

        private void DeactivateDevice()
        {
            if (!Dialogs.Confirm(
                "Désactiver la licence sur cet appareil ? Vos données ne seront pas supprimées, " +
                "mais une réactivation sera nécessaire au prochain démarrage."))
            {
                return;
            }

            _services.Licensing.Deactivate();
            RefreshLicense();
            Dialogs.Info("Licence désactivée sur cet appareil. Veuillez redémarrer l'application.");
        }

        private void ActivateModule()
        {
            var viewModel = new ModuleActivationViewModel(_services);
            var window = new ModuleActivationWindow
            {
                DataContext = viewModel,
                Owner = Application.Current.MainWindow
            };
            App.ApplyFlowDirection(window);
            viewModel.CloseRequested = ok => window.DialogResult = ok;
            window.ShowDialog();
            RefreshLicense();
        }

        private async void CheckUpdates()
        {
            System.Version version = Assembly.GetExecutingAssembly().GetName().Version;
            string current = version != null ? version.ToString(3) : "1.0.0";

            if (_services.Update == null || !_services.Update.IsSupported)
            {
                Dialogs.Info(
                    "Les mises à jour automatiques s'activeront après l'installation via le programme d'installation officiel.\r\n\r\n" +
                    "Version actuelle : " + current, "Mises à jour");
                return;
            }

            AppUpdateCheck check = await _services.Update.CheckForUpdatesAsync(CancellationToken.None);
            RefreshLicense();

            if (check.UpdateAvailable)
            {
                ((App)Application.Current).ShowUpdateDialog(check);
            }
            else
            {
                Dialogs.Info("Vous utilisez la dernière version (" + check.CurrentVersion + ").", "Mises à jour");
            }
        }

        private void ApplyLanguage(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code == _services.Localization.CurrentLanguage)
            {
                return;
            }

            _services.Settings.SetLanguage(code);
            _services.Localization.SetLanguage(code);
            App.ApplyFlowDirection(Application.Current.MainWindow);
        }

        private void Backup()
        {
            Result<BackupRecord> result = _services.Backup.Backup(BackupType.Manual);
            if (result.IsSuccess)
            {
                Dialogs.Info("Sauvegarde créée :\r\n" + result.Value.FilePath);
            }
            else
            {
                Dialogs.Error(result.Error);
            }
        }

        private void Restore()
        {
            var dialog = new OpenFileDialog { Filter = "Base OptiPaie (*.db)|*.db", Title = "Restaurer une sauvegarde" };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (!Dialogs.Confirm("La restauration remplacera les données actuelles. Continuer ?"))
            {
                return;
            }

            Result result = _services.Backup.Restore(dialog.FileName);
            if (result.IsSuccess)
            {
                Dialogs.Info("Restauration effectuée. Veuillez redémarrer l'application.");
            }
            else
            {
                Dialogs.Error(result.Error);
            }
        }
    }
}
