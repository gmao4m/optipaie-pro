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
        private readonly List<Employee> _all = new List<Employee>();

        private Company _selectedCompany;
        private Employee _selectedEmployee;
        private string _search = string.Empty;

        public EmployeesViewModel(AppServices services)
        {
            _services = services;
            Companies = new ObservableCollection<Company>();
            Employees = new ObservableCollection<Employee>();

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit);
            DeleteCommand = new RelayCommand(Delete);
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

        public void OnActivated()
        {
            Companies.Clear();
            foreach (Company c in _services.Companies.GetAll())
            {
                Companies.Add(c);
            }

            if (SelectedCompany == null || Companies.All(c => c.Id != SelectedCompany.Id))
            {
                SelectedCompany = Companies.FirstOrDefault();
            }
            else
            {
                LoadEmployees();
            }
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
