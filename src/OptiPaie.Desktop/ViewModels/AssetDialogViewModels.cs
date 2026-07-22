using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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

        private string _name;
        private AssetCategoryOption _category;
        private string _serial;
        private DateTime? _purchaseDate;
        private string _value;
        private string _notes;
        private bool _isShared;

        public AssetEditViewModel(AppServices services, long companyId, Asset existing)
        {
            _services = services;
            _companyId = companyId;
            _asset = existing ?? new Asset();

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
                Title = "Modifier le matériel";
            }
            else
            {
                _category = Categories.FirstOrDefault(o => o.Value == AssetCategory.Laptop);
                _purchaseDate = DateTime.Today;
                _value = "0";
                Title = "Nouveau matériel";
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

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            if (!decimal.TryParse((_value ?? string.Empty).Replace(" ", "").Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value))
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

    /// <summary>Read-only assignment history of one asset.</summary>
    public sealed class AssetHistoryViewModel : ObservableObject
    {
        private string _statusMessage = string.Empty;

        public AssetHistoryViewModel(AppServices services, long assetId, string assetName)
        {
            AssetName = assetName;

            foreach (AssetAssignmentSummary a in services.Assets.GetHistory(assetId))
            {
                History.Add(a);
            }

            _statusMessage = History.Count + " attribution(s)";
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        public Action RequestClose { get; set; }

        public string AssetName { get; }
        public ObservableCollection<AssetAssignmentSummary> History { get; } = new ObservableCollection<AssetAssignmentSummary>();
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand CloseCommand { get; }
    }
}
