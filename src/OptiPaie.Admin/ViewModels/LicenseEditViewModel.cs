using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Newtonsoft.Json;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Common;
using OptiPaie.Admin.Mvvm;

namespace OptiPaie.Admin.ViewModels
{
    /// <summary>A selectable company-scope option (Mono/Multi) with a friendly label.</summary>
    public sealed class ScopeOption
    {
        public ScopeOption(string value, string label) { Value = value; Label = label; }
        public string Value { get; }
        public string Label { get; }
    }

    public sealed class LicenseEditViewModel : ObservableObject
    {
        private static string _payrollProductId;

        private readonly License _license;
        private readonly bool _isNew;

        private string _company, _email, _type, _status, _scope;
        private string _generatedKey = string.Empty, _generatedScopeInfo = string.Empty;
        private bool _created;
        private bool _saving;
        private int _maxDevices = 1;
        private DateTime? _expires;

        public LicenseEditViewModel(License license)
        {
            _license = license ?? new License { Type = "lifetime", Status = "pending", MaxDevices = 1 };
            _isNew = license == null;
            _company = _license.CompanyName ?? string.Empty;
            _email = _license.Email ?? string.Empty;
            // Only two durations remain (lifetime / annual); normalise any legacy type.
            _type = _license.Type == "annual" ? "annual" : "lifetime";
            _status = _license.Status ?? "pending";
            _maxDevices = _license.MaxDevices <= 0 ? 1 : _license.MaxDevices;
            _expires = ParseDate(_license.ExpiresAt);
            // Company scope. New licenses default to Mono (the safe choice); existing
            // ones read the stored value (0 in DB = Multi-sociétés; 1/null = Mono).
            _scope = _isNew ? "mono" : (_license.MaxCompanies == 0 ? "multi" : "mono");

            SaveCommand = new RelayCommand(async () => await SaveAsync());
            EnableCommand = new RelayCommand(async () => await SetStatusAsync("active"));
            DisableCommand = new RelayCommand(async () => await SetStatusAsync("suspended"));
            ExtendCommand = new RelayCommand(async () => await ExtendAsync());
            ResetDevicesCommand = new RelayCommand(async () => await ResetDevicesAsync());
            DeleteCommand = new RelayCommand(async () => await DeleteAsync());
            CopyKeyCommand = new RelayCommand(CopyKey);
            // Every licence unlocks every section — there is no per-module selection,
            // no module-activation keys, and nothing module-related to load here.
        }

        public Action RequestClose { get; set; }

        public string Title => _isNew ? "Nouvelle licence" : _license.LicenseKey;
        public bool IsExisting => !_isNew;
        public string LicenseKey => _license.LicenseKey;

        public List<string> Types { get; } = new List<string> { "trial", "lifetime", "annual", "monthly", "demo", "enterprise" };
        public List<string> Statuses { get; } = new List<string> { "pending", "active", "suspended", "revoked" };

        /// <summary>
        /// The only two license types: Mono-société (1 company) vs Multi-sociétés
        /// (unlimited). BOTH unlock every feature — the sole difference is company count.
        /// </summary>
        public List<ScopeOption> ScopeOptions { get; } = new List<ScopeOption>
        {
            new ScopeOption("mono", "🏢 Mono-société — 1 entreprise (toutes les fonctionnalités)"),
            new ScopeOption("multi", "🏢🏢 Multi-sociétés — illimité (toutes les fonctionnalités)")
        };

        /// <summary>Duration choices, mapped onto the stored `type` value.</summary>
        public List<ScopeOption> Durations { get; } = new List<ScopeOption>
        {
            new ScopeOption("lifetime", "À vie (permanente)"),
            new ScopeOption("annual", "Annuelle — 1 an")
        };

        public string CompanyName { get => _company; set => Set(ref _company, value); }
        public string Email { get => _email; set => Set(ref _email, value); }

        /// <summary>
        /// The duration/type. Choosing "annual"/"monthly" pre-fills the expiry (+1 an /
        /// +1 mois) if empty; "lifetime"/"demo"/"enterprise" clears it (permanent).
        /// </summary>
        public string Type
        {
            get => _type;
            set { if (Set(ref _type, value)) ApplyDuration(); }
        }

        /// <summary>Company scope: "mono" or "multi".</summary>
        public string Scope { get => _scope; set => Set(ref _scope, value); }

        public string Status { get => _status; set => Set(ref _status, value); }
        public int MaxDevices { get => _maxDevices; set => Set(ref _maxDevices, value); }
        public DateTime? Expires { get => _expires; set => Set(ref _expires, value); }

        /// <summary>The freshly generated key (shown with a Copy button after creation).</summary>
        public string GeneratedKey { get => _generatedKey; private set { if (Set(ref _generatedKey, value)) Raise(nameof(HasGeneratedKey)); } }
        public bool HasGeneratedKey => !string.IsNullOrEmpty(_generatedKey);
        public string GeneratedScopeInfo { get => _generatedScopeInfo; private set => Set(ref _generatedScopeInfo, value); }

        public ICommand SaveCommand { get; }
        public ICommand EnableCommand { get; }
        public ICommand DisableCommand { get; }
        public ICommand ExtendCommand { get; }
        public ICommand ResetDevicesCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CopyKeyCommand { get; }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            // A new licence is created exactly once. Both guards are set SYNCHRONOUSLY
            // before any await, so a fast double-click during the network round-trip cannot
            // enter twice and mint two keys.
            if (_created || _saving) return;
            _saving = true;

            try
            {
                // Authoritative expiry from the duration, and the scope column.
                // Multi = 0 (unlimited), Mono = 1. Always an explicit value (never null),
                // so a missing token field can only ever mean Mono on the client.
                string expiresIso = ComputeExpiryIso();
                int maxCompanies = _scope == "multi" ? 0 : 1;

                if (_isNew)
                {
                    string productId = await EnsureProductAsync();
                    string key = await App.Api.RpcAsync<string>("gen_license_key", new { });
                    await App.Api.InsertAsync("licenses", new
                    {
                        product_id = productId,
                        license_key = key,
                        company_name = _company,
                        email = _email,
                        type = _type,
                        status = _status,
                        max_devices = _maxDevices,
                        max_companies = maxCompanies,
                        expires_at = expiresIso
                    });

                    // Show the key inline with a Copy button (keep the window open) instead
                    // of a popup; block further saves so no duplicate key is minted.
                    _created = true;
                    GeneratedScopeInfo =
                        (maxCompanies == 0 ? "Multi-sociétés (illimité)" : "Mono-société") + "  •  " +
                        (_type == "annual" ? "Annuelle (1 an)" : "À vie") + "  •  toutes les fonctionnalités";
                    GeneratedKey = key;
                }
                else
                {
                    await App.Api.UpdateAsync("licenses", "id=eq." + _license.Id, new
                    {
                        company_name = _company,
                        email = _email,
                        type = _type,
                        status = _status,
                        max_devices = _maxDevices,
                        max_companies = maxCompanies,
                        expires_at = expiresIso
                    });
                    RequestClose?.Invoke();
                }
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
            finally { _saving = false; }
        }

        /// <summary>Copies the freshly generated key to the clipboard.</summary>
        private void CopyKey()
        {
            if (string.IsNullOrEmpty(_generatedKey)) return;
            try { System.Windows.Clipboard.SetText(_generatedKey); }
            catch (Exception ex) { Dialogs.Error("Copie impossible : " + ex.Message); }
        }

        /// <summary>
        /// Pre-fills the expiry when the duration implies one, so the owner never has to
        /// compute a date: annual → +1 an, monthly → +1 mois (only if empty);
        /// lifetime/demo/enterprise → cleared (permanent).
        /// </summary>
        private void ApplyDuration()
        {
            switch ((_type ?? string.Empty).ToLowerInvariant())
            {
                case "annual": if (!_expires.HasValue) Expires = DateTime.Today.AddYears(1); break;
                case "monthly": if (!_expires.HasValue) Expires = DateTime.Today.AddMonths(1); break;
                case "lifetime":
                case "demo":
                case "enterprise": Expires = null; break;
                // trial: left as-is (the 48-hour trial is handled offline on the client).
            }
        }

        /// <summary>The expiry to persist, made authoritative by the duration at save time.</summary>
        private string ComputeExpiryIso()
        {
            DateTime? expires = _expires;
            switch ((_type ?? string.Empty).ToLowerInvariant())
            {
                case "lifetime":
                case "demo":
                case "enterprise": expires = null; break;
                case "annual": if (!expires.HasValue) expires = DateTime.Today.AddYears(1); break;
                case "monthly": if (!expires.HasValue) expires = DateTime.Today.AddMonths(1); break;
            }

            return expires.HasValue ? expires.Value.ToUniversalTime().ToString("o") : null;
        }

        private async System.Threading.Tasks.Task SetStatusAsync(string status)
        {
            if (_isNew) { Status = status; return; }
            try { await App.Api.UpdateAsync("licenses", "id=eq." + _license.Id, new { status }); Status = status; }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task ExtendAsync()
        {
            if (_isNew) return;
            DateTime baseDate = _expires ?? DateTime.UtcNow;
            Expires = baseDate.AddDays(30);
            try { await App.Api.UpdateAsync("licenses", "id=eq." + _license.Id, new { expires_at = _expires.Value.ToUniversalTime().ToString("o") }); }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task ResetDevicesAsync()
        {
            if (_isNew) return;
            if (!Dialogs.Confirm("Réinitialiser tous les appareils de cette licence ?")) return;
            try { await App.Api.DeleteAsync("devices", "license_id=eq." + _license.Id); Dialogs.Info("Appareils réinitialisés."); }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task DeleteAsync()
        {
            if (_isNew) { RequestClose?.Invoke(); return; }
            if (!Dialogs.Confirm("Supprimer définitivement cette licence ?")) return;
            try { await App.Api.DeleteAsync("licenses", "id=eq." + _license.Id); Dialogs.Info("Licence supprimée."); RequestClose?.Invoke(); }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private static async System.Threading.Tasks.Task<string> EnsureProductAsync()
        {
            if (string.IsNullOrEmpty(_payrollProductId))
            {
                var p = await App.Api.SelectSingleAsync<ProductRow>("products", "key=eq.payroll&select=id");
                _payrollProductId = p != null ? p.id : null;
            }
            return _payrollProductId;
        }

        private static DateTime? ParseDate(string iso)
        {
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime d)
                ? (DateTime?)d.ToLocalTime() : null;
        }

        private sealed class ProductRow { public string id { get; set; } }
    }
}
