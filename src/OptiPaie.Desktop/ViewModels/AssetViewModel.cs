using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One asset as shown in the list, with its current holder.</summary>
    public sealed class AssetRowViewModel
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public AssetRowViewModel(AssetSummary summary)
        {
            Summary = summary;
        }

        public AssetSummary Summary { get; }
        public long Id => Summary.AssetId;
        public string Name => Summary.Name;
        public string CategoryLabel => AssetLabels.Category(Summary.Category);
        public string StatusLabel => AssetLabels.Status(Summary.Status);
        public string SerialNumber => Summary.SerialNumber;
        public string ValueText => Summary.PurchaseValue.ToString("N2", Fr);
        public string HolderName => Summary.HolderName ?? "—";
        public bool IsAvailable => Summary.Status == AssetStatus.Available;
        public bool IsAssigned => Summary.Status == AssetStatus.Assigned;
    }

    /// <summary>French labels for the asset enums.</summary>
    public static class AssetLabels
    {
        public static string Category(AssetCategory category)
        {
            switch (category)
            {
                case AssetCategory.Laptop: return "Ordinateur";
                case AssetCategory.Phone: return "Téléphone";
                case AssetCategory.Vehicle: return "Véhicule";
                case AssetCategory.Uniform: return "Tenue / EPI";
                case AssetCategory.Tool: return "Outillage";
                default: return "Autre";
            }
        }

        public static string Status(AssetStatus status)
        {
            switch (status)
            {
                case AssetStatus.Available: return "Disponible";
                case AssetStatus.Assigned: return "Attribué";
                case AssetStatus.UnderRepair: return "En réparation";
                case AssetStatus.Retired: return "Réformé";
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// Matériel — company assets and who holds them. Assignments reference the shared
    /// employees, so the holder shown is always the live employee record.
    /// </summary>
    public sealed class AssetViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;

        private Company _selectedCompany;
        private AssetRowViewModel _selectedAsset;
        private string _availableText = "0";
        private string _assignedText = "0";
        private string _valueText = "0";
        private string _statusMessage = string.Empty;

        public AssetViewModel(AppServices services)
        {
            _services = services;

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selectedAsset != null);
            AssignCommand = new RelayCommand(Assign, () => _selectedAsset != null && _selectedAsset.IsAvailable);
            ReturnCommand = new RelayCommand(Return, () => _selectedAsset != null && _selectedAsset.IsAssigned);
            HistoryCommand = new RelayCommand(OpenHistory, () => _selectedAsset != null);
            RepairCommand = new RelayCommand(() => SetStatus(AssetStatus.UnderRepair), () => _selectedAsset != null && _selectedAsset.IsAvailable);
            AvailableCommand = new RelayCommand(() => SetStatus(AssetStatus.Available), () => _selectedAsset != null && !_selectedAsset.IsAssigned && !_selectedAsset.IsAvailable);
            RetireCommand = new RelayCommand(() => SetStatus(AssetStatus.Retired), () => _selectedAsset != null && !_selectedAsset.IsAssigned);
            DeleteCommand = new RelayCommand(Delete, () => _selectedAsset != null && !_selectedAsset.IsAssigned);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<AssetRowViewModel> Assets { get; } = new ObservableCollection<AssetRowViewModel>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Load(); }
        }

        public AssetRowViewModel SelectedAsset
        {
            get => _selectedAsset;
            set => Set(ref _selectedAsset, value);
        }

        public string AvailableText { get => _availableText; private set => Set(ref _availableText, value); }
        public string AssignedText { get => _assignedText; private set => Set(ref _assignedText, value); }
        public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand AssignCommand { get; }
        public ICommand ReturnCommand { get; }
        public ICommand HistoryCommand { get; }
        public ICommand RepairCommand { get; }
        public ICommand AvailableCommand { get; }
        public ICommand RetireCommand { get; }
        public ICommand DeleteCommand { get; }

        public void OnActivated()
        {
            // The active company comes from the single global selector in the header.
            _selectedCompany = _services.CompanyContext.Active;
            Raise(nameof(SelectedCompany));
            Load();
        }

        private void Load()
        {
            Assets.Clear();
            if (_selectedCompany == null)
            {
                AvailableText = AssignedText = ValueText = "0";
                return;
            }

            int available = 0, assigned = 0;
            decimal totalValue = 0m;

            foreach (AssetSummary summary in _services.Assets.GetByCompany(_selectedCompany.Id))
            {
                Assets.Add(new AssetRowViewModel(summary));
                totalValue += summary.PurchaseValue;
                if (summary.Status == AssetStatus.Available) available++;
                if (summary.Status == AssetStatus.Assigned) assigned++;
            }

            SelectedAsset = Assets.FirstOrDefault();
            AvailableText = available.ToString();
            AssignedText = assigned.ToString();
            ValueText = totalValue.ToString("N2", Fr);
            StatusMessage = Assets.Count + " matériel(s) · " + assigned + " attribué(s)";
        }

        private void New()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info("Sélectionnez d'abord une entreprise.");
                return;
            }

            ShowEditor(new AssetEditViewModel(_services, _selectedCompany.Id, null));
        }

        private void Edit()
        {
            ShowEditor(new AssetEditViewModel(_services, _selectedCompany.Id, _services.Assets.Get(_selectedAsset.Id)));
        }

        private void ShowEditor(AssetEditViewModel vm)
        {
            var window = new AssetEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                Load();
                StatusMessage = "Matériel enregistré.";
            }
        }

        private void Assign()
        {
            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            if (employees.Count == 0)
            {
                Dialogs.Info("Aucun employé actif dans cette entreprise.");
                return;
            }

            var vm = new AssetAssignViewModel(employees);
            var window = new AssetAssignWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() != true || vm.SelectedEmployee == null) return;

            Run(_services.Assets.Assign(_selectedAsset.Id, vm.SelectedEmployee.Id, vm.Date, vm.Condition, vm.Notes),
                "Matériel attribué à " + (vm.SelectedEmployee.LastNameFr + " " + vm.SelectedEmployee.FirstNameFr).Trim() + ".");
        }

        private void Return()
        {
            var vm = new AssetReturnViewModel();
            var window = new AssetReturnWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() != true) return;

            Run(_services.Assets.Return(_selectedAsset.Id, vm.Date, vm.Condition), "Retour enregistré.");
        }

        private void OpenHistory()
        {
            var vm = new AssetHistoryViewModel(_services, _selectedAsset.Id, _selectedAsset.Name);
            var window = new AssetHistoryWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
        }

        private void SetStatus(AssetStatus status) =>
            Run(_services.Assets.SetStatus(_selectedAsset.Id, status), "Statut mis à jour.");

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer définitivement ce matériel et son historique ?"))
            {
                return;
            }

            Run(_services.Assets.Delete(_selectedAsset.Id), "Matériel supprimé.");
        }

        private void Run(Result result, string success)
        {
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Load();
            StatusMessage = success;
        }
    }
}
