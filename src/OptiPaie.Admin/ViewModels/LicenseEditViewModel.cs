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
    public sealed class ModuleToggle : ObservableObject
    {
        private readonly Action<ModuleToggle, bool> _onChange;
        private bool _enabled;
        private readonly bool _live;

        public ModuleToggle(string key, string name, bool core, bool enabled, string info, Action<ModuleToggle, bool> onChange)
        {
            Key = key; Name = name; IsCore = core; _enabled = enabled; Info = info; _onChange = onChange; _live = true;
        }

        public string Key { get; }
        public string Name { get; }
        public bool IsCore { get; }
        public bool CanToggle => !IsCore;
        public string Info { get; }

        public bool Enabled
        {
            get => _enabled;
            set { if (Set(ref _enabled, value) && _live && !IsCore) _onChange(this, value); }
        }
    }

    public sealed class LicenseEditViewModel : ObservableObject
    {
        private static string _payrollProductId;

        private readonly License _license;
        private readonly bool _isNew;

        private string _company, _email, _type, _status, _newKeyInfo = string.Empty;
        private int _maxDevices = 1;
        private DateTime? _expires;
        private string _selectedModule = "ats";
        private DateTime? _keyExpiry;

        public LicenseEditViewModel(License license)
        {
            _license = license ?? new License { Type = "lifetime", Status = "pending", MaxDevices = 1 };
            _isNew = license == null;
            _company = _license.CompanyName ?? string.Empty;
            _email = _license.Email ?? string.Empty;
            _type = _license.Type ?? "lifetime";
            _status = _license.Status ?? "pending";
            _maxDevices = _license.MaxDevices <= 0 ? 1 : _license.MaxDevices;
            _expires = ParseDate(_license.ExpiresAt);

            SaveCommand = new RelayCommand(async () => await SaveAsync());
            EnableCommand = new RelayCommand(async () => await SetStatusAsync("active"));
            DisableCommand = new RelayCommand(async () => await SetStatusAsync("suspended"));
            ExtendCommand = new RelayCommand(async () => await ExtendAsync());
            ResetDevicesCommand = new RelayCommand(async () => await ResetDevicesAsync());
            DeleteCommand = new RelayCommand(async () => await DeleteAsync());
            GenerateKeyCommand = new RelayCommand(async () => await GenerateKeyAsync());
            RevokeKeyCommand = new RelayCommand(async o => await RevokeKeyAsync(o as ActivationKey));

            if (!_isNew) LoadDetailsAsync();
        }

        public Action RequestClose { get; set; }

        public string Title => _isNew ? "Nouvelle licence" : _license.LicenseKey;
        public bool IsExisting => !_isNew;
        public string LicenseKey => _license.LicenseKey;

        public List<string> Types { get; } = new List<string> { "trial", "lifetime", "annual", "monthly", "demo", "enterprise" };
        public List<string> Statuses { get; } = new List<string> { "pending", "active", "suspended", "revoked" };
        public List<Modules.Item> UpsellModules { get; } = Modules.All.FindAll(m => !m.Core);

        public string CompanyName { get => _company; set => Set(ref _company, value); }
        public string Email { get => _email; set => Set(ref _email, value); }
        public string Type { get => _type; set => Set(ref _type, value); }
        public string Status { get => _status; set => Set(ref _status, value); }
        public int MaxDevices { get => _maxDevices; set => Set(ref _maxDevices, value); }
        public DateTime? Expires { get => _expires; set => Set(ref _expires, value); }
        public string SelectedModule { get => _selectedModule; set => Set(ref _selectedModule, value); }
        public DateTime? KeyExpiry { get => _keyExpiry; set => Set(ref _keyExpiry, value); }
        public string NewKeyInfo { get => _newKeyInfo; private set => Set(ref _newKeyInfo, value); }

        public ObservableCollection<ModuleToggle> ModuleList { get; } = new ObservableCollection<ModuleToggle>();
        public ObservableCollection<ActivationKey> Keys { get; } = new ObservableCollection<ActivationKey>();

        public ICommand SaveCommand { get; }
        public ICommand EnableCommand { get; }
        public ICommand DisableCommand { get; }
        public ICommand ExtendCommand { get; }
        public ICommand ResetDevicesCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand GenerateKeyCommand { get; }
        public ICommand RevokeKeyCommand { get; }

        private async void LoadDetailsAsync()
        {
            try
            {
                var perms = await App.Api.SelectAsync<ModulePermission>(
                    "license_modules", "license_id=eq." + _license.Id + "&select=module_key,enabled,activated_at,expires_at");
                ModuleList.Clear();
                foreach (Modules.Item m in Modules.All)
                {
                    ModulePermission p = Array.Find(perms, x => x.ModuleKey == m.Key);
                    bool on = m.Core || (p != null && p.Enabled);
                    string info = p != null && !string.IsNullOrEmpty(p.ActivatedAt) ? "activé le " + Dates.Short(p.ActivatedAt) : string.Empty;
                    ModuleList.Add(new ModuleToggle(m.Key, m.Name, m.Core, on, info, OnModuleChanged));
                }
                await LoadKeysAsync();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task LoadKeysAsync()
        {
            Keys.Clear();
            foreach (ActivationKey k in await App.Api.SelectAsync<ActivationKey>(
                "activation_keys", "license_id=eq." + _license.Id + "&select=*&order=created_at.desc"))
            {
                Keys.Add(k);
            }
        }

        private async void OnModuleChanged(ModuleToggle toggle, bool enabled)
        {
            try
            {
                await App.Api.UpsertAsync("license_modules", new
                {
                    license_id = _license.Id,
                    product_id = _license.ProductId,
                    module_key = toggle.Key,
                    enabled,
                    activated_at = enabled ? DateTime.UtcNow.ToString("o") : null
                }, "license_id,module_key");
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            try
            {
                var patch = new
                {
                    company_name = _company,
                    email = _email,
                    type = _type,
                    status = _status,
                    max_devices = _maxDevices,
                    expires_at = _expires.HasValue ? _expires.Value.ToUniversalTime().ToString("o") : null
                };

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
                        expires_at = patch.expires_at
                    });
                    Dialogs.Info("Licence créée : " + key);
                }
                else
                {
                    await App.Api.UpdateAsync("licenses", "id=eq." + _license.Id, patch);
                }

                RequestClose?.Invoke();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
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

        private async System.Threading.Tasks.Task GenerateKeyAsync()
        {
            if (_isNew) { Dialogs.Info("Enregistrez d'abord la licence."); return; }
            try
            {
                var rows = await App.Api.RpcAsync<GenRow[]>("generate_module_keys", new
                {
                    p_license_key = _license.LicenseKey,
                    p_module_key = _selectedModule,
                    p_count = 1,
                    p_expires = _keyExpiry.HasValue ? _keyExpiry.Value.ToUniversalTime().ToString("o") : null
                });
                string code = rows != null && rows.Length > 0 ? rows[0].key_code : "";
                NewKeyInfo = "Clé générée : " + code;
                await LoadKeysAsync();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task RevokeKeyAsync(ActivationKey key)
        {
            if (key == null) return;
            if (!Dialogs.Confirm("Révoquer cette clé d'activation ?")) return;
            try { await App.Api.RpcAsync<object>("revoke_activation_key", new { p_key_id = key.Id }); await LoadKeysAsync(); }
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

        private sealed class GenRow { public string key_code { get; set; } }
        private sealed class ProductRow { public string id { get; set; } }
    }
}
