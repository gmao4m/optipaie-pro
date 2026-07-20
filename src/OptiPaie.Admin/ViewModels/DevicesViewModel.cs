using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Common;
using OptiPaie.Admin.Mvvm;

namespace OptiPaie.Admin.ViewModels
{
    public sealed class DevicesViewModel : SectionViewModel
    {
        public DevicesViewModel()
        {
            RefreshCommand = new RelayCommand(() => Load());
            ResetCommand = new RelayCommand(async o => await ResetAsync(o as Device));
        }

        public ObservableCollection<Device> Items { get; } = new ObservableCollection<Device>();
        public ICommand RefreshCommand { get; }
        public ICommand ResetCommand { get; }

        public override async void Load()
        {
            Busy = true;
            try
            {
                Items.Clear();
                foreach (Device d in await App.Api.SelectAsync<Device>(
                    "devices", "select=*,licenses(license_key,company_name)&order=last_seen_at.desc&limit=300"))
                {
                    Items.Add(d);
                }
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
            finally { Busy = false; }
        }

        private async System.Threading.Tasks.Task ResetAsync(Device device)
        {
            if (device == null) return;
            if (!Dialogs.Confirm("Libérer cet appareil ? La licence pourra être réactivée ailleurs.")) return;
            try { await App.Api.DeleteAsync("devices", "id=eq." + device.Id); Load(); }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }
    }
}
