using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OptiPaie.Admin.Api;
using OptiPaie.Admin.Common;
using OptiPaie.Admin.Mvvm;

namespace OptiPaie.Admin.ViewModels
{
    public sealed class AuditViewModel : SectionViewModel
    {
        private const int PageSize = 30;

        private string _actionFilter = string.Empty;
        private string _pageInfo = string.Empty;
        private int _page;
        private int _total;

        public AuditViewModel()
        {
            RefreshCommand = new RelayCommand(() => { _page = 0; Reload(); });
            PrevCommand = new RelayCommand(() => { if (_page > 0) { _page--; Reload(); } }, () => _page > 0);
            NextCommand = new RelayCommand(() => { _page++; Reload(); }, () => (_page + 1) * PageSize < _total);
        }

        public ObservableCollection<AuditRow> Items { get; } = new ObservableCollection<AuditRow>();
        public List<string> Actions { get; } = new List<string>
        {
            "", "license.generate", "license.activate", "license.validate",
            "module.activate", "module_key.generate", "module_key.revoke",
            "module.validation_failed", "activation.error", "validation.error"
        };

        public string ActionFilter { get => _actionFilter; set { if (Set(ref _actionFilter, value)) { _page = 0; Reload(); } } }
        public string PageInfo { get => _pageInfo; private set => Set(ref _pageInfo, value); }

        public ICommand RefreshCommand { get; }
        public ICommand PrevCommand { get; }
        public ICommand NextCommand { get; }

        public override void Load() { _page = 0; Reload(); }

        private async void Reload()
        {
            Busy = true;
            try
            {
                string q = "select=*&order=created_at.desc";
                if (!string.IsNullOrEmpty(_actionFilter)) q += "&action=eq." + _actionFilter;
                PagedResult<AuditRow> res = await App.Api.SelectPagedAsync<AuditRow>(
                    "audit_log", q, _page * PageSize, _page * PageSize + PageSize - 1);
                Items.Clear();
                foreach (AuditRow a in res.Items) Items.Add(a);
                _total = res.Total;
                int pages = Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize));
                PageInfo = _total + " entrée(s) · page " + (_page + 1) + "/" + pages;
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
            finally { Busy = false; }
        }
    }
}
