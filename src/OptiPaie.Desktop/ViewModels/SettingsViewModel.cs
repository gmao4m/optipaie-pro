using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            ToggleLegalDeductionsCommand = new RelayCommand(ToggleLegalDeductions, () => CanEditLegalDeductions);
            SyncLicenseCommand = new RelayCommand(SyncLicense, () => !IsSyncingLicense);
            RemoveDemoCommand = new RelayCommand(RemoveDemo, () => CanRemoveDemo);
        }

        // -- Trial demo company removal --------------------------------------
        private bool _canRemoveDemo;
        private string _demoCompanyText = string.Empty;

        /// <summary>
        /// True only for an administrator AND when the trial demo company is positively identified
        /// (flag, or full fingerprint + exact roster). Otherwise the block is not shown at all.
        /// </summary>
        public bool CanRemoveDemo { get => _canRemoveDemo; private set => Set(ref _canRemoveDemo, value); }

        public string DemoCompanyText { get => _demoCompanyText; private set => Set(ref _demoCompanyText, value); }

        public ICommand RemoveDemoCommand { get; }

        /// <summary>The preview + confirmation. Replaceable so the flow can be exercised headless.</summary>
        public Func<DemoRemovalPreviewViewModel, bool> ConfirmDemoRemoval { get; set; } = preview =>
        {
            var window = new DemoRemovalWindow { DataContext = preview, Owner = Application.Current?.MainWindow };
            return window.ShowDialog() == true;
        };

        /// <summary>Shows the outcome. Replaceable so the flow can be exercised headless.</summary>
        public Action<string> ShowDemoMessage { get; set; } = message => Dialogs.Info(message, "Démonstration · العرض التجريبي");

        private void RefreshDemo()
        {
            OptiPaie.Services.DemoCompanyInfo demo = null;
            if (_services.DemoData != null && ScreenAccess.CanEdit(_services, "settings"))
            {
                try { demo = _services.DemoData.FindDemoCompany(); }
                catch (Exception ex) { _services.Logger?.Warn("Demo detection failed: " + ex.Message); }
            }

            DemoCompanyText = demo != null ? demo.Name : string.Empty;
            CanRemoveDemo = demo != null;
            CommandManager.InvalidateRequerySuggested();
        }

        private void RemoveDemo()
        {
            var t = OptiPaie.Desktop.Localization.TranslationSource.Instance;
            OptiPaie.Services.DemoDeletionPreview preview = _services.DemoData.Preview();
            if (preview == null)
            {
                ShowDemoMessage(t["Demo_NotFound"]);
                RefreshDemo();
                return;
            }

            // The preview window shows the counts and, when the demo is the only company, the refusal
            // (its Delete button is then disabled). The service re-checks everything anyway.
            if (!ConfirmDemoRemoval(new DemoRemovalPreviewViewModel(preview, _services.Localization.IsRightToLeft)) ||
                preview.IsOnlyCompany)
            {
                return;
            }

            Result<OptiPaie.Services.DemoDeletionOutcome> result;
            try
            {
                result = _services.DemoData.Delete();
            }
            catch (OptiPaie.Core.Security.AccessDeniedException)
            {
                Dialogs.Error(t["Legal_ReadOnly"]);
                return;
            }

            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                RefreshDemo();
                return;
            }

            long deleted = result.Value.Deleted.Where(r => r.Table != "Candidates.HiredEmployeeId").Sum(r => r.Rows);
            ShowDemoMessage(string.Format(CultureInfo.InvariantCulture, t["Demo_Done"],
                deleted.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")), result.Value.BackupPath));

            // If the demo was the open company, switch to the remaining one (the shell rebuilds itself
            // on an active-company change, so no screen keeps a byte of the removed company).
            CompanyContext companies = _services.CompanyContext;
            bool wasActive = companies.ActiveId == preview.Company.Id;
            companies.Reload();
            if (wasActive && result.Value.RemainingCompanyId.HasValue)
            {
                Company target = companies.Companies.FirstOrDefault(c => c.Id == result.Value.RemainingCompanyId.Value);
                if (target != null) companies.Active = target;
            }

            RefreshDemo();
        }

        // -- IRG / CNAS switch ------------------------------------------------
        private bool _legalDeductionsOn = true;
        private bool _canEditLegalDeductions;

        /// <summary>Current value of the IRG/CNAS setting (fail-safe read: anything but OFF is ON).</summary>
        public bool LegalDeductionsOn { get => _legalDeductionsOn; private set => Set(ref _legalDeductionsOn, value); }

        /// <summary>Administrators only (Permission.ManageSettings). Everyone else sees it read-only.</summary>
        public bool CanEditLegalDeductions { get => _canEditLegalDeductions; private set => Set(ref _canEditLegalDeductions, value); }

        public ICommand ToggleLegalDeductionsCommand { get; }

        /// <summary>
        /// Asks before switching OFF (new payslips only; saved ones never change). Replaceable so the
        /// flow can be exercised headless; defaults to the themed confirmation window.
        /// </summary>
        public Func<bool> ConfirmTurnOff { get; set; } = () =>
        {
            var window = new LegalDeductionsConfirmWindow { Owner = Application.Current?.MainWindow };
            return window.ShowDialog() == true;
        };

        private void RefreshLegalDeductions()
        {
            LegalDeductionsOn = _services.ConfigurationService.GetLegalDeductionsEnabled();
            CanEditLegalDeductions = ScreenAccess.CanEdit(_services, "settings");
        }

        private void ToggleLegalDeductions()
        {
            bool target = !LegalDeductionsOn;
            if (!target && !ConfirmTurnOff()) return; // turning OFF needs a confirmation; ON does not

            try
            {
                Result result = _services.ConfigurationService.SetLegalDeductionsEnabled(target);
                if (result.IsFailure) Dialogs.Error(result.Error);
            }
            catch (OptiPaie.Core.Security.AccessDeniedException)
            {
                Dialogs.Error(OptiPaie.Desktop.Localization.TranslationSource.Instance["Legal_ReadOnly"]);
            }

            RefreshLegalDeductions(); // always re-read what is actually stored
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

            // The PRODUCT version (AssemblyInformationalVersion, e.g. "1.33.0"), NOT the AssemblyVersion
            // which is frozen at 1.8.0.0 for binding stability. Same source as the update system, so the
            // footer and the update dialog always agree — support gets the real installed version.
            VersionText = "OptiPaie PRO  ·  version " + OptiPaie.Core.Updates.AppVersion.CurrentProduct();

            RefreshLegalDeductions();
            RefreshLicense();
            RefreshDemo();
        }

        // -- "تحديث الترخيص": force a licence check now ---------------------------
        private bool _isSyncingLicense;

        /// <summary>
        /// Checks the licence with the server immediately (no waiting for the startup / 24 h check), for a
        /// customer who calls while his licence is being upgraded. A Mono ⇄ Multi change is announced by the
        /// shell (one place for every check); this reports the check itself. Offline/failure never changes
        /// the entitlement.
        /// </summary>
        public ICommand SyncLicenseCommand { get; }

        public bool IsSyncingLicense
        {
            get => _isSyncingLicense;
            private set { if (Set(ref _isSyncingLicense, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        /// <summary>Shows the check report. Replaceable so the flow can be exercised headless.</summary>
        public Action<string> ShowLicenseReport { get; set; } = message => Dialogs.Info(message, "Licence · الترخيص");

        /// <summary>Completes when the last licence check finished (lets a test await the button).</summary>
        public System.Threading.Tasks.Task LastLicenseSync { get; private set; } = System.Threading.Tasks.Task.CompletedTask;

        private void SyncLicense() => LastLicenseSync = SyncLicenseAsync();

        private async System.Threading.Tasks.Task SyncLicenseAsync()
        {
            if (IsSyncingLicense) return;
            IsSyncingLicense = true;
            LicenseSnapshot before = _services.Licensing.Current;
            LicenseResult result;
            try
            {
                result = await _services.Licensing.SynchronizeAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _services.Logger?.Warn("Licence refresh failed: " + ex.Message);
                result = LicenseResult.Create(LicenseResultKind.Error, string.Empty, _services.Licensing.Current);
            }
            finally
            {
                IsSyncingLicense = false;
            }

            LicenseSnapshot after = _services.Licensing.Current;
            RefreshLicense();
            if (OptiPaie.Services.Licensing.LicenseScopeChange.Describe(before, after) == null)
                ShowLicenseReport(OptiPaie.Services.Licensing.LicenseScopeChange.RefreshReport(result, before, after));
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

        /// <summary>
        /// Opens the account / activation page (Task 2). The activation window creates the
        /// account and activates a key on first use, or renews with a new key when the annual
        /// license has expired; an already-active, signed-in customer can still enter a
        /// renewal key here. The email is pre-filled from the local account for convenience.
        /// </summary>
        private void RenewLicense()
        {
            var viewModel = new ActivationViewModel(_services, ActivationMode.Activate, _services.CustomerAccount.Email);
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

        private async void CheckUpdates()
        {
            // The PRODUCT version (InformationalVersion) — the SAME field the updater uses — NOT
            // GetName().Version, which was frozen at the 1.8.0.0 placeholder.
            string current = OptiPaie.Core.Updates.AppVersion.CurrentProduct();

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
