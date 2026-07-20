using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Common;
using OptiPaie.Admin.Mvvm;
using OptiPaie.Admin.Views;

namespace OptiPaie.Admin.ViewModels
{
    public sealed class LicensesViewModel : SectionViewModel
    {
        private const int PageSize = 20;

        private string _search = string.Empty;
        private string _statusFilter = string.Empty;
        private string _typeFilter = string.Empty;
        private string _pageInfo = string.Empty;
        private int _page;
        private int _total;

        public LicensesViewModel()
        {
            RefreshCommand = new RelayCommand(() => { _page = 0; Reload(); });
            NewCommand = new RelayCommand(() => OpenEditor(null));
            BulkCommand = new RelayCommand(OpenBulk);
            OpenCommand = new RelayCommand(o => OpenEditor(o as License));
            PrevCommand = new RelayCommand(() => { if (_page > 0) { _page--; Reload(); } }, () => _page > 0);
            NextCommand = new RelayCommand(() => { _page++; Reload(); }, () => (_page + 1) * PageSize < _total);
        }

        public ObservableCollection<License> Items { get; } = new ObservableCollection<License>();
        public List<string> StatusFilters { get; } = new List<string> { "", "active", "suspended", "revoked", "pending" };
        public List<string> TypeFilters { get; } = new List<string> { "", "trial", "lifetime", "annual", "monthly", "demo", "enterprise" };

        public string Search { get => _search; set => Set(ref _search, value); }
        public string StatusFilter { get => _statusFilter; set { if (Set(ref _statusFilter, value)) { _page = 0; Reload(); } } }
        public string TypeFilter { get => _typeFilter; set { if (Set(ref _typeFilter, value)) { _page = 0; Reload(); } } }
        public string PageInfo { get => _pageInfo; private set => Set(ref _pageInfo, value); }

        public ICommand RefreshCommand { get; }
        public ICommand NewCommand { get; }
        public ICommand BulkCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand PrevCommand { get; }
        public ICommand NextCommand { get; }

        public override void Load() { _page = 0; Reload(); }

        private async void Reload()
        {
            Busy = true;
            try
            {
                string q = "select=*&order=created_at.desc";
                if (!string.IsNullOrWhiteSpace(_search))
                {
                    string p = "*" + Uri.EscapeDataString(_search.Trim()) + "*";
                    q += "&or=(license_key.ilike." + p + ",company_name.ilike." + p + ",email.ilike." + p + ")";
                }
                if (!string.IsNullOrEmpty(_statusFilter)) q += "&status=eq." + _statusFilter;
                if (!string.IsNullOrEmpty(_typeFilter)) q += "&type=eq." + _typeFilter;

                PagedResult<License> res = await App.Api.SelectPagedAsync<License>("licenses", q, _page * PageSize, _page * PageSize + PageSize - 1);
                Items.Clear();
                foreach (License l in res.Items) Items.Add(l);
                _total = res.Total;
                int pages = Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize));
                PageInfo = _total + " licence(s) · page " + (_page + 1) + "/" + pages;
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
            finally { Busy = false; }
        }

        private void OpenEditor(License license)
        {
            var vm = new LicenseEditViewModel(license);
            var window = new LicenseEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
            Reload();
        }

        private void OpenBulk()
        {
            var vm = new BulkViewModel();
            var window = new BulkWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
            Reload();
        }
    }
}
