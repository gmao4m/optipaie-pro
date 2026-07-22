using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One contract as shown in the list, with its derived expiry position.</summary>
    public sealed class ContractRowViewModel
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public ContractRowViewModel(ContractSummary summary, string employeeName)
        {
            Summary = summary;
            EmployeeName = employeeName;
        }

        public ContractSummary Summary { get; }
        public long Id => Summary.ContractId;
        public string EmployeeName { get; }
        public string TypeLabel => EnumLabels.ContractLabel(Summary.Type);
        public string StatusLabel => ContractLabels.Status(Summary.Status);
        public string Position => Summary.Position;
        public string SalaryText => Summary.BaseSalary.ToString("N2", Fr);
        public string StartText => Summary.StartDate.ToString("dd/MM/yyyy", Fr);
        public string EndText => Summary.EndDate.HasValue ? Summary.EndDate.Value.ToString("dd/MM/yyyy", Fr) : "—";

        public string ExpiryText
        {
            get
            {
                if (!Summary.DaysUntilExpiry.HasValue) return "—";
                if (Summary.IsOverdue) return "En retard de " + (-Summary.DaysUntilExpiry.Value) + " j";
                if (Summary.Status == ContractStatus.Active) return "Dans " + Summary.DaysUntilExpiry.Value + " j";
                return "—";
            }
        }

        public bool IsActive => Summary.Status == ContractStatus.Active;
        public bool IsDraft => Summary.Status == ContractStatus.Draft;
        public bool CanRenew => Summary.Status == ContractStatus.Active || Summary.Status == ContractStatus.Expired;
    }

    /// <summary>French labels for the contract status enum.</summary>
    public static class ContractLabels
    {
        public static string Status(ContractStatus status)
        {
            switch (status)
            {
                case ContractStatus.Draft: return "Préparation";
                case ContractStatus.Active: return "En vigueur";
                case ContractStatus.Expired: return "Expiré";
                case ContractStatus.Terminated: return "Résilié";
                case ContractStatus.Renewed: return "Renouvelé";
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// Contrats — employment contracts of a company. Activating a contract writes its
    /// salary, type and position onto the shared employee, so payroll uses the terms in
    /// force. Fixed-term contracts nearing their end are surfaced as alerts.
    /// </summary>
    public sealed class ContractViewModel : ObservableObject, IActivable
    {
        private const int AlertWindowDays = 30;

        private readonly AppServices _services;
        private readonly Dictionary<long, string> _employeeNames = new Dictionary<long, string>();

        private Company _selectedCompany;
        private ContractRowViewModel _selectedContract;
        private string _activeCountText = "0";
        private string _expiringCountText = "0";
        private string _statusMessage = string.Empty;

        public ContractViewModel(AppServices services)
        {
            _services = services;

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selectedContract != null && _selectedContract.IsDraft);
            ActivateCommand = new RelayCommand(Activate, () => _selectedContract != null && _selectedContract.IsDraft);
            TerminateCommand = new RelayCommand(Terminate, () => _selectedContract != null && _selectedContract.IsActive);
            RenewCommand = new RelayCommand(Renew, () => _selectedContract != null && _selectedContract.CanRenew);
            PdfCommand = new RelayCommand(ExportPdf, () => _selectedContract != null);
            DeleteCommand = new RelayCommand(Delete, () => _selectedContract != null && !_selectedContract.IsActive);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<ContractRowViewModel> Contracts { get; } = new ObservableCollection<ContractRowViewModel>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Load(); }
        }

        public ContractRowViewModel SelectedContract
        {
            get => _selectedContract;
            set => Set(ref _selectedContract, value);
        }

        public string ActiveCountText { get => _activeCountText; private set => Set(ref _activeCountText, value); }
        public string ExpiringCountText { get => _expiringCountText; private set => Set(ref _expiringCountText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ActivateCommand { get; }
        public ICommand TerminateCommand { get; }
        public ICommand RenewCommand { get; }
        public ICommand PdfCommand { get; }
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
            Contracts.Clear();
            _employeeNames.Clear();

            if (_selectedCompany == null)
            {
                ActiveCountText = ExpiringCountText = "0";
                return;
            }

            foreach (Employee employee in _services.Employees.GetByCompany(_selectedCompany.Id))
            {
                _employeeNames[employee.Id] = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
            }

            int active = 0;
            foreach (ContractSummary summary in _services.Contracts.GetByCompany(_selectedCompany.Id))
            {
                _employeeNames.TryGetValue(summary.EmployeeId, out string name);
                Contracts.Add(new ContractRowViewModel(summary, name ?? "—"));
                if (summary.Status == ContractStatus.Active) active++;
            }

            int expiring = _services.Contracts.GetExpiring(_selectedCompany.Id, AlertWindowDays).Count;

            SelectedContract = Contracts.FirstOrDefault();
            ActiveCountText = active.ToString();
            ExpiringCountText = expiring.ToString();
            StatusMessage = Contracts.Count + " contrat(s) · " + active + " en vigueur"
                + (expiring > 0 ? " · " + expiring + " à renouveler sous " + AlertWindowDays + " j" : string.Empty);
        }

        private void New()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info("Sélectionnez d'abord une entreprise.");
                return;
            }

            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            if (employees.Count == 0)
            {
                Dialogs.Info("Aucun employé actif dans cette entreprise.");
                return;
            }

            ShowEditor(new ContractEditViewModel(_services, employees, null));
        }

        private void Edit()
        {
            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            ShowEditor(new ContractEditViewModel(_services, employees, _services.Contracts.Get(_selectedContract.Id)));
        }

        private void ShowEditor(ContractEditViewModel vm)
        {
            var window = new ContractEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                Load();
                StatusMessage = "Contrat enregistré.";
            }
        }

        private void Activate()
        {
            if (!Dialogs.Confirm("Activer ce contrat ? Ses termes (salaire, type, poste) seront appliqués à l'employé."))
            {
                return;
            }

            Run(_services.Contracts.Activate(_selectedContract.Id),
                "Contrat activé — les termes ont été appliqués à l'employé.");
        }

        private void Terminate()
        {
            var vm = new ContractTerminateViewModel();
            var window = new ContractTerminateWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() != true) return;

            Run(_services.Contracts.Terminate(_selectedContract.Id, vm.EffectiveDate, vm.Reason),
                "Contrat résilié — la date de sortie de l'employé a été enregistrée.");
        }

        private void Renew()
        {
            EmploymentContract current = _services.Contracts.Get(_selectedContract.Id);
            var vm = new ContractRenewViewModel(current);
            var window = new ContractRenewWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() != true) return;

            Result<long> result = _services.Contracts.Renew(
                _selectedContract.Id, vm.NewStart, vm.NewEnd, vm.NewSalary);

            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            Load();
            StatusMessage = "Contrat renouvelé — un nouveau contrat en vigueur a été créé.";
        }

        private void ExportPdf()
        {
            EmploymentContract contract = _services.Contracts.Get(_selectedContract.Id);
            Employee employee = _services.Employees.Get(contract.EmployeeId);
            if (employee == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = "Contrat_" + Sanitize(_selectedContract.EmployeeName) + ".pdf"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var document = new ContractDocument(new ContractDocumentModel
                {
                    Company = _selectedCompany,
                    Employee = employee,
                    Contract = contract
                });

                Document.Create(document.Compose).GeneratePdf(dialog.FileName);
                Open(dialog.FileName);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export PDF contrat", ex);
                Dialogs.Error("Impossible de générer le PDF : " + ex.Message);
            }
        }

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer définitivement ce contrat ?"))
            {
                return;
            }

            Run(_services.Contracts.Delete(_selectedContract.Id), "Contrat supprimé.");
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

        private void Open(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _services.Logger.Warn("Ouverture du PDF impossible : " + ex.Message);
                Dialogs.Info("Fichier enregistré :" + Environment.NewLine + path);
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Employe";
            var chars = value.Where(c => Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) < 0 && c != ' ')
                .DefaultIfEmpty('_').ToArray();
            return new string(chars);
        }
    }
}
