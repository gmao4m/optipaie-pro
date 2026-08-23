using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>An asset category with its French label.</summary>
    public sealed class AssetCategoryOption
    {
        public AssetCategoryOption(AssetCategory value) { Value = value; Label = AssetLabels.Category(value); }
        public AssetCategory Value { get; }
        public string Label { get; }
    }

    /// <summary>Creates or edits an asset (status is driven by assign/return, not here).</summary>
    public sealed class AssetEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Asset _asset;
        private readonly long _companyId;
        private readonly bool _isNew;

        private string _name;
        private AssetCategoryOption _category;
        private string _serial;
        private DateTime? _purchaseDate;
        private string _value;
        private string _notes;
        private bool _isShared;

        // Optional immediate hand-over (creation only): the picker is filled from the
        // active company's employees, and choosing one assigns the asset on save.
        private readonly ObservableCollection<Employee> _employees = new ObservableCollection<Employee>();
        private ICollectionView _employeesView;
        private Employee _selectedHolder;
        private DateTime? _handoverDate;
        private string _holderSearch;

        public AssetEditViewModel(AppServices services, long companyId, Asset existing)
        {
            _services = services;
            _companyId = companyId;
            _asset = existing ?? new Asset();
            _isNew = existing == null;

            foreach (AssetCategory c in Enum.GetValues(typeof(AssetCategory))) Categories.Add(new AssetCategoryOption(c));

            if (existing != null)
            {
                _name = existing.Name;
                _category = Categories.FirstOrDefault(o => o.Value == existing.Category);
                _serial = existing.SerialNumber;
                _purchaseDate = existing.PurchaseDate;
                _value = existing.PurchaseValue.ToString(CultureInfo.InvariantCulture);
                _notes = existing.Notes;
                _isShared = existing.IsShared;
                Title = services.Localization.GetString("Asset_Edit");
            }
            else
            {
                _category = Categories.FirstOrDefault(o => o.Value == AssetCategory.Laptop);
                _purchaseDate = DateTime.Today;
                _value = "0";
                _handoverDate = DateTime.Today;
                Title = services.Localization.GetString("Asset_New");

                // Populate the holder picker from the active company's employees so a
                // brand-new asset can be handed over on the spot (empty state handled in XAML).
                foreach (Employee e in services.Employees.GetByCompany(companyId, false)) _employees.Add(e);
                _employeesView = CollectionViewSource.GetDefaultView(_employees);
                _employeesView.Filter = FilterHolder;
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }
        public string Title { get; }

        public ObservableCollection<AssetCategoryOption> Categories { get; } = new ObservableCollection<AssetCategoryOption>();

        public string Name { get => _name; set => Set(ref _name, value); }
        public AssetCategoryOption Category { get => _category; set => Set(ref _category, value); }
        public string SerialNumber { get => _serial; set => Set(ref _serial, value); }
        public DateTime? PurchaseDate { get => _purchaseDate; set => Set(ref _purchaseDate, value); }
        public string PurchaseValue { get => _value; set => Set(ref _value, value); }
        public string Notes { get => _notes; set => Set(ref _notes, value); }

        /// <summary>Shared: several employees may hold this asset at once (e.g. a pool vehicle).</summary>
        public bool IsShared { get => _isShared; set => Set(ref _isShared, value); }

        /// <summary>True only when creating — drives the optional hand-over section's visibility.</summary>
        public bool IsNew => _isNew;

        /// <summary>True when the active company has at least one employee to hand the asset to.</summary>
        public bool HasEmployees => _employees.Count > 0;

        /// <summary>True once a holder is picked — reveals the hand-over date field.</summary>
        public bool HasHolder => _selectedHolder != null;

        /// <summary>The (filterable) employee picker for the optional immediate hand-over.</summary>
        public ICollectionView Employees => _employeesView;

        /// <summary>The employee to hand the new asset to, or null to leave it available.</summary>
        public Employee SelectedHolder
        {
            get => _selectedHolder;
            set { if (Set(ref _selectedHolder, value)) Raise(nameof(HasHolder)); }
        }

        /// <summary>Hand-over date, defaulting to today; only used when a holder is chosen.</summary>
        public DateTime? HandoverDate { get => _handoverDate; set => Set(ref _handoverDate, value); }

        /// <summary>Type-to-filter text for the holder picker.</summary>
        public string HolderSearch
        {
            get => _holderSearch;
            set { if (Set(ref _holderSearch, value)) _employeesView?.Refresh(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private bool FilterHolder(object item)
        {
            if (string.IsNullOrWhiteSpace(_holderSearch)) return true;
            var e = item as Employee;
            if (e == null) return false;
            if (e == _selectedHolder) return true; // never hide the current pick
            string q = _holderSearch.Trim();
            string full = ((e.LastNameFr ?? "") + " " + (e.FirstNameFr ?? "")).Trim();
            return full.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Save()
        {
            if (!OptiPaie.Common.Text.FlexibleNumber.TryParse(_value, out decimal value))
            {
                Dialogs.Error("Valeur invalide.");
                return;
            }

            _asset.CompanyId = _companyId;
            _asset.Name = _name;
            _asset.Category = _category != null ? _category.Value : AssetCategory.Other;
            _asset.SerialNumber = _serial;
            _asset.PurchaseDate = _purchaseDate;
            _asset.PurchaseValue = value;
            _asset.Notes = _notes;
            _asset.IsShared = _isShared;

            Result<long> result = _services.Assets.Save(_asset);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            // If a holder was chosen on a brand-new asset, hand it over now. The act of
            // assigning is what sets the status to Attribué and writes the first history row.
            if (_isNew && _selectedHolder != null)
            {
                Result assign = _services.Assets.Assign(result.Value, _selectedHolder.Id,
                    _handoverDate ?? DateTime.Today, null, null);
                if (assign.IsFailure)
                {
                    // The asset was created; only the hand-over failed. Report it and keep the
                    // asset (it stays Disponible so the user can assign it from the list).
                    Dialogs.Error(assign.Error);
                }
            }

            RequestClose?.Invoke(true);
        }
    }

    /// <summary>Dialog for handing an asset to an employee.</summary>
    public sealed class AssetAssignViewModel : ObservableObject
    {
        private Employee _selectedEmployee;
        private DateTime _date = DateTime.Today;
        private string _condition;
        private string _notes;

        public AssetAssignViewModel(IReadOnlyList<Employee> employees)
        {
            foreach (Employee e in employees) Employees.Add(e);
            _selectedEmployee = Employees.FirstOrDefault();

            ConfirmCommand = new RelayCommand(() => RequestClose?.Invoke(true));
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();

        public Employee SelectedEmployee { get => _selectedEmployee; set => Set(ref _selectedEmployee, value); }
        public DateTime Date { get => _date; set => Set(ref _date, value); }
        public string Condition { get => _condition; set => Set(ref _condition, value); }
        public string Notes { get => _notes; set => Set(ref _notes, value); }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
    }

    /// <summary>
    /// Dialog for recording an asset return. For a shared asset held by several employees
    /// it shows a holder picker so exactly one holder is returned; otherwise the single
    /// holder is implied.
    /// </summary>
    public sealed class AssetReturnViewModel : ObservableObject
    {
        private DateTime _date = DateTime.Today;
        private string _condition;
        private AssetAssignmentSummary _selectedHolder;

        public AssetReturnViewModel(IReadOnlyList<AssetAssignmentSummary> holders = null)
        {
            if (holders != null)
            {
                foreach (AssetAssignmentSummary h in holders) Holders.Add(h);
                _selectedHolder = Holders.Count > 0 ? Holders[0] : null;
            }

            ConfirmCommand = new RelayCommand(() => RequestClose?.Invoke(true));
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }

        /// <summary>The current holders (populated only when a choice is needed).</summary>
        public ObservableCollection<AssetAssignmentSummary> Holders { get; } = new ObservableCollection<AssetAssignmentSummary>();

        /// <summary>True when several employees hold the asset and one must be chosen.</summary>
        public bool HasHolderChoice => Holders.Count > 1;

        public AssetAssignmentSummary SelectedHolder { get => _selectedHolder; set => Set(ref _selectedHolder, value); }

        public DateTime Date { get => _date; set => Set(ref _date, value); }
        public string Condition { get => _condition; set => Set(ref _condition, value); }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
    }

    /// <summary>
    /// Asset detail view: the asset's current status and holder, its facts, the full
    /// assignment history, and the first-class hand-over / return / edit actions. Opened by
    /// double-clicking a row (or its chevron), it is the hub for one asset.
    /// </summary>
    public sealed class AssetHistoryViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly long _companyId;
        private readonly long _assetId;

        private string _statusMessage = string.Empty;
        private string _assetName;
        private string _statusLabel;
        private string _statusKind = "neutral";
        private string _holderLine = "—";
        private string _categoryLabel;
        private string _valueText;
        private string _serialNumber = "—";
        private bool _isAvailable;
        private bool _isAssigned;

        public AssetHistoryViewModel(AppServices services, long companyId, long assetId)
        {
            _services = services;
            _companyId = companyId;
            _assetId = assetId;

            AssignCommand = new RelayCommand(() => Act(AssetActions.Assign(_services, _companyId, _assetId)), () => _isAvailable);
            ReturnCommand = new RelayCommand(() => Act(AssetActions.Return(_services, _assetId)), () => _isAssigned);
            EditCommand = new RelayCommand(() => Act(AssetActions.Edit(_services, _companyId, _assetId)));
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Reload();
        }

        public Action RequestClose { get; set; }

        public ObservableCollection<AssetAssignmentSummary> History { get; } = new ObservableCollection<AssetAssignmentSummary>();

        public string AssetName { get => _assetName; private set => Set(ref _assetName, value); }
        public string StatusLabel { get => _statusLabel; private set => Set(ref _statusLabel, value); }
        public string StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }
        public string HolderLine { get => _holderLine; private set => Set(ref _holderLine, value); }
        public string CategoryLabel { get => _categoryLabel; private set => Set(ref _categoryLabel, value); }
        public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }
        public string SerialNumber { get => _serialNumber; private set => Set(ref _serialNumber, value); }
        public bool IsAvailable { get => _isAvailable; private set => Set(ref _isAvailable, value); }
        public bool IsAssigned { get => _isAssigned; private set => Set(ref _isAssigned, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand AssignCommand { get; }
        public ICommand ReturnCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand CloseCommand { get; }

        private void Act(bool changed)
        {
            if (changed) Reload();
        }

        private void Reload()
        {
            AssetSummary summary = _services.Assets.GetSummary(_assetId);
            if (summary != null)
            {
                AssetName = summary.Name;
                StatusLabel = AssetLabels.Status(summary.Status);
                StatusKind = KindOf(summary.Status);
                CategoryLabel = AssetLabels.Category(summary.Category);
                ValueText = summary.PurchaseValue.ToString("N2", Fr);
                SerialNumber = string.IsNullOrWhiteSpace(summary.SerialNumber) ? "—" : summary.SerialNumber;
                IsAvailable = summary.Status == AssetStatus.Available;
                IsAssigned = summary.Status == AssetStatus.Assigned;
            }

            History.Clear();
            IReadOnlyList<AssetAssignmentSummary> all = _services.Assets.GetHistory(_assetId);
            foreach (AssetAssignmentSummary a in all) History.Add(a);

            var open = all.Where(a => a.ReturnedDate == null).ToList();
            HolderLine = open.Count == 0
                ? "—"
                : (open.Count == 1 ? open[0].EmployeeName : open.Count + " détenteurs");

            StatusMessage = History.Count + " attribution(s)";
            CommandManager.InvalidateRequerySuggested();
        }

        private static string KindOf(AssetStatus status)
        {
            switch (status)
            {
                case AssetStatus.Available: return "success";
                case AssetStatus.Assigned: return "accent";
                case AssetStatus.UnderRepair: return "pending";
                default: return "neutral";
            }
        }
    }
}
