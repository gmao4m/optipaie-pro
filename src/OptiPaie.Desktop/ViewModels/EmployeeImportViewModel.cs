using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One line of the mapping step: our field (left) ↔ a column from the user's file (right).</summary>
    public sealed class ImportMappingRow : ObservableObject
    {
        private readonly Action _changed;
        private string _selectedHeader;

        public ImportMappingRow(string key, string label, bool required, IReadOnlyList<string> available, string selected, Action changed)
        {
            Key = key; Label = label; Required = required;
            Available = available;
            _selectedHeader = selected;
            _changed = changed;
        }

        public string Key { get; }
        public string Label { get; }
        public bool Required { get; }

        /// <summary>The file's detected headers, plus an empty "(ignore)" entry at the top.</summary>
        public IReadOnlyList<string> Available { get; }

        /// <summary>The chosen source header, or empty to leave the field unmapped.</summary>
        public string SelectedHeader
        {
            get => _selectedHeader;
            set { if (Set(ref _selectedHeader, value)) { Raise(nameof(IsUnmappedRequired)); _changed?.Invoke(); } }
        }

        public bool IsMapped => !string.IsNullOrEmpty(_selectedHeader);

        /// <summary>Required but nothing chosen → blocks the import (highlighted in the UI).</summary>
        public bool IsUnmappedRequired => Required && !IsMapped;
    }

    /// <summary>
    /// Import an employee file with column mapping. One screen: map your columns (auto-matched by
    /// name, FR/AR), see the live preview (new / duplicates / errors, each problem located), pick how
    /// to treat duplicates, confirm. Validate-before-write and the atomic all-or-nothing commit are
    /// the service's — unchanged. The last mapping is remembered per company.
    /// </summary>
    public sealed class EmployeeImportViewModel : ObservableObject
    {
        private const string Ignore = "—";

        private readonly AppServices _services;
        private readonly long _companyId;
        private readonly string _filePath;
        private readonly ImportTable _table;
        private bool _loading;

        private int _newCount, _dupCount, _errCount;
        private string _status = string.Empty;
        private bool _skipDuplicates = true;

        public EmployeeImportViewModel(AppServices services, long companyId, string filePath, ImportTable table)
        {
            _services = services;
            _companyId = companyId;
            _filePath = filePath;
            _table = table;

            Mapping = new ObservableCollection<ImportMappingRow>();
            Issues = new ObservableCollection<ImportIssue>();
            RecheckCommand = new RelayCommand(Recompute);
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            BuildMappingRows();
            Recompute();
        }

        public Action<bool> RequestClose { get; set; }

        public ObservableCollection<ImportMappingRow> Mapping { get; }
        public ObservableCollection<ImportIssue> Issues { get; }
        public ICommand RecheckCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string Title => L("Import_Title");
        public string Subtitle => L("Import_Subtitle");
        public string FileName => System.IO.Path.GetFileName(_filePath);

        public string SummaryText => string.Format(L("Import_Summary"), _newCount, _dupCount, _errCount);
        public bool HasErrors => _errCount > 0;
        public bool HasDuplicates => _dupCount > 0;
        public bool HasIssues => Issues.Count > 0;

        /// <summary>Required fields still unmapped — blocks import, named in the warning.</summary>
        public IReadOnlyList<string> UnmappedRequired => Mapping.Where(m => m.IsUnmappedRequired).Select(m => m.Label).ToList();
        public bool HasUnmappedRequired => UnmappedRequired.Count > 0;
        public string UnmappedRequiredText => HasUnmappedRequired
            ? string.Format(L("Import_RequiredMissing"), string.Join(", ", UnmappedRequired)) : string.Empty;

        public bool SkipDuplicates { get => _skipDuplicates; set => Set(ref _skipDuplicates, value); }
        public bool StopOnDuplicates { get => !_skipDuplicates; set => SkipDuplicates = !value; }

        public string Status { get => _status; private set => Set(ref _status, value); }

        /// <summary>Import allowed only when no required column is unmapped and no row is in error.</summary>
        public bool CanConfirm => !HasUnmappedRequired && !HasErrors && (_newCount + _dupCount) > 0;

        private void BuildMappingRows()
        {
            var headers = new List<string> { Ignore };
            headers.AddRange(_table.Headers);

            IReadOnlyDictionary<string, string> saved = LoadSavedMapping();

            foreach (ImportColumn col in _services.EmployeeImportExport.Columns)
            {
                string selected = AutoMatch(col, saved);
                Mapping.Add(new ImportMappingRow(col.Key, col.Header, col.Required, headers,
                    string.IsNullOrEmpty(selected) ? Ignore : selected, OnMappingChanged));
            }
        }

        private string AutoMatch(ImportColumn col, IReadOnlyDictionary<string, string> saved)
        {
            // 1) a remembered choice from last time (if that header is still in the file)
            if (saved != null && saved.TryGetValue(col.Key, out string s) && _table.Headers.Any(h => Norm(h) == Norm(s)))
                return _table.Headers.First(h => Norm(h) == Norm(s));

            // 2) auto-match by the combined bilingual header, either language, or the field key
            foreach (string candidate in new[] { col.Header, col.HeaderFr, col.HeaderAr, col.Key })
            {
                string hit = _table.Headers.FirstOrDefault(h => Norm(h) == Norm(candidate));
                if (hit != null) return hit;
            }
            return string.Empty;
        }

        private static string Norm(string s) => (s ?? string.Empty).Trim().Replace("  ", " ").ToLowerInvariant();

        private void OnMappingChanged()
        {
            if (_loading) return;
            Recompute();
        }

        private IReadOnlyDictionary<string, string> CurrentMapping()
        {
            var map = new Dictionary<string, string>();
            foreach (ImportMappingRow row in Mapping)
                if (row.IsMapped && row.SelectedHeader != Ignore) map[row.Key] = row.SelectedHeader;
            return map;
        }

        private void Recompute()
        {
            _loading = true;
            try
            {
                ImportPreview preview = _services.EmployeeImportExport.BuildPreview(_companyId, _table, CurrentMapping());
                _newCount = preview.NewCount;
                _dupCount = preview.DuplicateCount;
                _errCount = preview.ErrorCount;

                Issues.Clear();
                foreach (ImportIssue i in preview.Issues.Take(200)) Issues.Add(i);

                RaiseAll();
            }
            finally { _loading = false; }
        }

        private void Confirm()
        {
            if (!CanConfirm) return;

            DuplicateResolution resolution = _skipDuplicates ? DuplicateResolution.SkipDuplicates : DuplicateResolution.Stop;
            Result<ImportSummary> result = _services.EmployeeImportExport.Commit(
                _companyId, _table, CurrentMapping(), resolution, FileName);

            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            SaveMapping();
            Dialogs.Info(string.Format(L("Employees_ImportDone"), result.Value.Created, result.Value.SkippedDuplicates));
            RequestClose?.Invoke(true);
        }

        // -- per-company remembered mapping (settings, no migration) --
        private string MapKey => "EmployeeImport.Mapping." + _companyId;

        private IReadOnlyDictionary<string, string> LoadSavedMapping()
        {
            var map = new Dictionary<string, string>();
            try
            {
                string raw = _services.Settings.Get(MapKey, string.Empty);
                foreach (string line in (raw ?? string.Empty).Split('\n'))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { /* best-effort */ }
            return map;
        }

        private void SaveMapping()
        {
            try
            {
                string serialized = string.Join("\n", CurrentMapping().Select(kv => kv.Key + "=" + kv.Value));
                _services.Settings.Set(MapKey, serialized);
            }
            catch { /* best-effort */ }
        }

        private void RaiseAll()
        {
            Raise(nameof(SummaryText)); Raise(nameof(HasErrors)); Raise(nameof(HasDuplicates)); Raise(nameof(HasIssues));
            Raise(nameof(UnmappedRequired)); Raise(nameof(HasUnmappedRequired)); Raise(nameof(UnmappedRequiredText));
            Raise(nameof(CanConfirm));
        }

        private string L(string key) => _services.Localization.GetString(key);
    }
}
