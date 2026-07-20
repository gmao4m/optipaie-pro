using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Common;
using OptiPaie.Admin.Mvvm;

namespace OptiPaie.Admin.ViewModels
{
    public sealed class UpdatesViewModel : SectionViewModel
    {
        private string _version = string.Empty;
        private string _channel = "stable";
        private string _packageUrl = string.Empty;
        private string _releaseNotes = string.Empty;
        private bool _mandatory;

        public UpdatesViewModel()
        {
            RefreshCommand = new RelayCommand(() => Load());
            PublishCommand = new RelayCommand(async () => await PublishAsync());
            SetLatestCommand = new RelayCommand(async o => await SetLatestAsync(o as UpdateRow));
            DeleteCommand = new RelayCommand(async o => await DeleteAsync(o as UpdateRow));
        }

        public ObservableCollection<UpdateRow> Items { get; } = new ObservableCollection<UpdateRow>();
        public List<string> Channels { get; } = new List<string> { "stable", "beta" };

        public string Version { get => _version; set => Set(ref _version, value); }
        public string Channel { get => _channel; set => Set(ref _channel, value); }
        public string PackageUrl { get => _packageUrl; set => Set(ref _packageUrl, value); }
        public string ReleaseNotes { get => _releaseNotes; set => Set(ref _releaseNotes, value); }
        public bool Mandatory { get => _mandatory; set => Set(ref _mandatory, value); }

        public ICommand RefreshCommand { get; }
        public ICommand PublishCommand { get; }
        public ICommand SetLatestCommand { get; }
        public ICommand DeleteCommand { get; }

        public override async void Load()
        {
            Busy = true;
            try
            {
                Items.Clear();
                foreach (UpdateRow u in await App.Api.SelectAsync<UpdateRow>("updates", "select=*&order=published_at.desc"))
                {
                    Items.Add(u);
                }
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
            finally { Busy = false; }
        }

        private async System.Threading.Tasks.Task PublishAsync()
        {
            if (string.IsNullOrWhiteSpace(_version)) { Dialogs.Error("La version est requise."); return; }
            try
            {
                await App.Api.InsertAsync("updates", new
                {
                    version = _version.Trim(),
                    channel = _channel,
                    mandatory = _mandatory,
                    package_url = _packageUrl,
                    release_notes = _releaseNotes
                });
                Version = string.Empty; PackageUrl = string.Empty; ReleaseNotes = string.Empty; Mandatory = false;
                Dialogs.Info("Version publiée.");
                Load();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task SetLatestAsync(UpdateRow update)
        {
            if (update == null) return;
            try
            {
                await App.Api.UpdateAsync("updates", "is_latest=eq.true", new { is_latest = false });
                await App.Api.UpdateAsync("updates", "id=eq." + update.Id, new { is_latest = true });
                await App.Api.UpsertAsync("app_settings", new { key = "latest_version", value = update.Version }, "key");
                Dialogs.Info("Version " + update.Version + " définie comme dernière.");
                Load();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        private async System.Threading.Tasks.Task DeleteAsync(UpdateRow update)
        {
            if (update == null) return;
            if (!Dialogs.Confirm("Supprimer cette version ?")) return;
            try { await App.Api.DeleteAsync("updates", "id=eq." + update.Id); Load(); }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }
    }
}
