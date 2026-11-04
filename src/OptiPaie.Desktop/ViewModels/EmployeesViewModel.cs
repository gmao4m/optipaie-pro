using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Employees module: a clean searchable list on the left and a profile on the right
    /// (no data grid). Add/edit happens in a focused dialog; delete is confirmed.
    /// </summary>
    public sealed class EmployeesViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;
        private readonly System.Action<string> _navigate;
        private readonly List<Employee> _all = new List<Employee>();

        private Company _selectedCompany;
        private Employee _selectedEmployee;
        private string _search = string.Empty;

        public EmployeesViewModel(AppServices services, System.Action<string> navigate = null)
        {
            _services = services;
            _navigate = navigate;
            Companies = new ObservableCollection<Company>();
            Employees = new ObservableCollection<Employee>();

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit);
            DeleteCommand = new RelayCommand(Delete);
            OpenProfileCommand = new RelayCommand(OpenProfile, () => _selectedEmployee != null);
            ImportCommand = new RelayCommand(Import);
            ExportCommand = new RelayCommand(Export);
            DownloadTemplateCommand = new RelayCommand(DownloadTemplate);
        }

        public ObservableCollection<Company> Companies { get; }
        public ObservableCollection<Employee> Employees { get; }

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) LoadEmployees(); }
        }

        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set { if (Set(ref _selectedEmployee, value)) Raise(nameof(HasSelection)); }
        }

        public bool HasSelection => _selectedEmployee != null;

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) ApplyFilter(); }
        }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand OpenProfileCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand DownloadTemplateCommand { get; }

        private void OpenProfile()
        {
            if (_selectedEmployee == null) return;
            Dialogs.ShowEmployeeProfile(new EmployeeProfileViewModel(_services, _selectedEmployee.Id, _navigate));
        }

        private string L(string key) => _services.Localization.GetString(key);

        private static OptiPaie.Core.Dtos.ImportFileFormat FormatOf(string path) =>
            path.EndsWith(".csv", System.StringComparison.OrdinalIgnoreCase)
                ? OptiPaie.Core.Dtos.ImportFileFormat.Csv : OptiPaie.Core.Dtos.ImportFileFormat.Xlsx;

        private void DownloadTemplate()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
                FileName = "modele_employes.xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _services.EmployeeImportExport.WriteTemplate(dlg.FileName, FormatOf(dlg.FileName));
                Dialogs.Info(L("Employees_TemplateSaved") + "\n" + dlg.FileName);
            }
            catch (System.Exception ex) { Dialogs.ErrorWithLog(L("Employees_IoError"), ex); }
        }

        private void Export()
        {
            if (SelectedCompany == null) { Dialogs.Error(L("Cnas_NoCompany")); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
                FileName = "employes_" + System.DateTime.Now.ToString("yyyyMMdd") + ".xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _services.EmployeeImportExport.Export(SelectedCompany.Id, dlg.FileName, FormatOf(dlg.FileName));
                Dialogs.Info(L("Employees_ExportDone") + "\n" + dlg.FileName);
            }
            catch (System.Exception ex) { Dialogs.ErrorWithLog(L("Employees_IoError"), ex); }
        }

        private void Import()
        {
            if (SelectedCompany == null) { Dialogs.Error(L("Cnas_NoCompany")); return; }

            // Step 1 — choose file.
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            OptiPaie.Core.Dtos.ImportTable table;
            try { table = _services.EmployeeImportExport.Read(dlg.FileName); }
            catch (System.Exception ex) { Dialogs.ErrorWithLog(L("Employees_IoError"), ex); return; }

            // Step 2 — map columns + review preview, Step 3 — confirm: all handled in one window.
            // A file whose headers differ from the template is NOT refused; the window's mapping step
            // lets the user match their columns to ours (auto-matched where possible, remembered per company).
            if (Dialogs.ShowEmployeeImport(new EmployeeImportViewModel(_services, SelectedCompany.Id, dlg.FileName, table)))
            {
                LoadEmployees();
            }
        }

        public void OnActivated()
        {
            // The active company comes from the single global selector in the header.
            _selectedCompany = _services.CompanyContext.Active;
            Raise(nameof(SelectedCompany));
            LoadEmployees();
        }

        private void LoadEmployees()
        {
            _all.Clear();
            if (SelectedCompany != null)
            {
                _all.AddRange(_services.Employees.GetByCompany(SelectedCompany.Id, true));
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            long? keepId = _selectedEmployee?.Id;

            IEnumerable<Employee> filtered = _all;
            if (!string.IsNullOrWhiteSpace(_search))
            {
                string q = _search.Trim().ToLowerInvariant();
                filtered = _all.Where(e =>
                    ((e.LastNameFr ?? string.Empty) + " " + (e.FirstNameFr ?? string.Empty)).ToLowerInvariant().Contains(q)
                    || ((e.LastNameAr ?? string.Empty) + " " + (e.FirstNameAr ?? string.Empty)).ToLowerInvariant().Contains(q)
                    || (e.Poste ?? string.Empty).ToLowerInvariant().Contains(q));
            }

            Employees.Clear();
            foreach (Employee e in filtered)
            {
                Employees.Add(e);
            }

            SelectedEmployee = Employees.FirstOrDefault(e => e.Id == keepId) ?? Employees.FirstOrDefault();
        }

        private void New()
        {
            if (SelectedCompany == null)
            {
                Dialogs.Error("Créez d'abord une entreprise, puis ajoutez des employés.");
                return;
            }

            var employee = new Employee
            {
                CompanyId = SelectedCompany.Id,
                IsActive = true,
                Gender = Core.Enums.Gender.Male,
                ContractType = Core.Enums.ContractType.Cdi,
                MaritalStatus = Core.Enums.MaritalStatus.Single,
                PaymentMode = Core.Enums.PaymentMode.BankTransfer,
                HireDate = System.DateTime.Today
            };

            if (Dialogs.ShowEmployeeEditor(new EmployeeEditViewModel(_services, employee, true)))
            {
                LoadEmployees();
            }
        }

        private void Edit()
        {
            if (SelectedEmployee == null)
            {
                return;
            }

            Employee full = _services.Employees.Get(SelectedEmployee.Id);
            if (full == null)
            {
                return;
            }

            if (Dialogs.ShowEmployeeEditor(new EmployeeEditViewModel(_services, full, false)))
            {
                LoadEmployees();
            }
        }

        private void Delete()
        {
            if (SelectedEmployee == null)
            {
                return;
            }

            if (!Dialogs.Confirm("Voulez-vous vraiment supprimer cet employé ?"))
            {
                return;
            }

            Result result = _services.Employees.Delete(SelectedEmployee.Id);
            if (result.IsSuccess)
            {
                LoadEmployees();
            }
            else
            {
                Dialogs.Error(result.Error);
            }
        }
    }
}
