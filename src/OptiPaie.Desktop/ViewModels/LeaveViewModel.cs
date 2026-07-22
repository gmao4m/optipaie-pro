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
    /// <summary>One leave request as shown in the list.</summary>
    public sealed class LeaveRowViewModel
    {
        public LeaveRowViewModel(LeaveRequest request, string employeeName)
        {
            Request = request;
            EmployeeName = employeeName;
        }

        public LeaveRequest Request { get; }
        public long Id => Request.Id;
        public string EmployeeName { get; }
        public string TypeLabel => LeaveLabels.Type(Request.Type);
        public string StatusLabel => LeaveLabels.Status(Request.Status);
        public string Period =>
            Request.StartDate.ToString("dd/MM/yyyy") + " → " + Request.EndDate.ToString("dd/MM/yyyy");
        public string DaysText => Request.Days.ToString("0.##", CultureInfo.InvariantCulture);
        public string Reason => Request.Reason;
        public bool IsPending => Request.Status == LeaveStatus.Pending;
        public bool IsApproved => Request.Status == LeaveStatus.Approved;
    }

    /// <summary>French labels for the leave enums (kept in one place).</summary>
    public static class LeaveLabels
    {
        public static string Type(LeaveType type)
        {
            switch (type)
            {
                case LeaveType.Annual: return "Congé annuel";
                case LeaveType.Sick: return "Congé maladie";
                case LeaveType.Unpaid: return "Congé sans solde";
                case LeaveType.Maternity: return "Congé maternité";
                case LeaveType.Special: return "Congé exceptionnel";
                default: return "Congé";
            }
        }

        public static string Status(LeaveStatus status)
        {
            switch (status)
            {
                case LeaveStatus.Pending: return "En attente";
                case LeaveStatus.Approved: return "Approuvé";
                case LeaveStatus.Rejected: return "Refusé";
                case LeaveStatus.Cancelled: return "Annulé";
                default: return string.Empty;
            }
        }
    }

    /// <summary>A leave type with its French label (for combo boxes).</summary>
    public sealed class LeaveTypeOption
    {
        public LeaveTypeOption(LeaveType value) { Value = value; Label = LeaveLabels.Type(value); }
        public LeaveType Value { get; }
        public string Label { get; }
    }

    /// <summary>
    /// Congés — requests of one company for one year, with the annual-leave balance of
    /// the selected employee. Approving here immediately writes the days into the
    /// Attendance module, so payroll picks them up with no further action.
    /// </summary>
    public sealed class LeaveViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;
        private readonly Dictionary<long, string> _employeeNames = new Dictionary<long, string>();

        private Company _selectedCompany;
        private int _selectedYear = DateTime.Today.Year;
        private LeaveRowViewModel _selectedRequest;
        private string _pendingText = "0", _takenText = "0", _remainingText = "0", _unpaidText = "0";
        private string _balanceCaption = string.Empty;
        private string _statusMessage = string.Empty;

        public LeaveViewModel(AppServices services)
        {
            _services = services;

            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selectedRequest != null && _selectedRequest.IsPending);
            ApproveCommand = new RelayCommand(Approve, () => _selectedRequest != null && _selectedRequest.IsPending);
            RejectCommand = new RelayCommand(Reject, () => _selectedRequest != null && _selectedRequest.IsPending);
            CancelCommand = new RelayCommand(CancelRequest, () => _selectedRequest != null && _selectedRequest.IsApproved);
            DeleteCommand = new RelayCommand(Delete, () => _selectedRequest != null);
            BalancesCommand = new RelayCommand(OpenBalances);
            SettingsCommand = new RelayCommand(OpenSettings);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<LeaveRowViewModel> Requests { get; } = new ObservableCollection<LeaveRowViewModel>();

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) Load(); }
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set { if (Set(ref _selectedYear, value)) Load(); }
        }

        public LeaveRowViewModel SelectedRequest
        {
            get => _selectedRequest;
            set { if (Set(ref _selectedRequest, value)) UpdateBalance(); }
        }

        public string PendingText { get => _pendingText; private set => Set(ref _pendingText, value); }
        public string TakenText { get => _takenText; private set => Set(ref _takenText, value); }
        public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }
        public string UnpaidText { get => _unpaidText; private set => Set(ref _unpaidText, value); }
        public string BalanceCaption { get => _balanceCaption; private set => Set(ref _balanceCaption, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RejectCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand BalancesCommand { get; }
        public ICommand SettingsCommand { get; }

        public void OnActivated()
        {
            // The active company comes from the single global selector in the header.
            _selectedCompany = _services.CompanyContext.Active;
            Raise(nameof(SelectedCompany));
            Load();
        }

        private void Load()
        {
            Requests.Clear();
            _employeeNames.Clear();

            if (_selectedCompany == null)
            {
                UpdateBalance();
                return;
            }

            foreach (Employee employee in _services.Employees.GetByCompany(_selectedCompany.Id))
            {
                _employeeNames[employee.Id] = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
            }

            foreach (LeaveRequest request in _services.Leave.GetByCompanyYear(_selectedCompany.Id, _selectedYear))
            {
                _employeeNames.TryGetValue(request.EmployeeId, out string name);
                Requests.Add(new LeaveRowViewModel(request, name ?? "—"));
            }

            SelectedRequest = Requests.FirstOrDefault();

            int pending = Requests.Count(r => r.IsPending);
            StatusMessage = Requests.Count + " demande(s) · " + pending + " en attente";
            UpdateBalance();
        }

        /// <summary>Shows the annual-leave position of the selected request's employee.</summary>
        private void UpdateBalance()
        {
            if (_selectedRequest == null)
            {
                PendingText = TakenText = RemainingText = UnpaidText = "0";
                BalanceCaption = "Sélectionnez une demande pour voir le solde de l'employé.";
                return;
            }

            LeaveBalance balance = _services.Leave.GetBalance(_selectedRequest.Request.EmployeeId, _selectedYear);

            PendingText = Num(balance.Pending);
            TakenText = Num(balance.Taken);
            RemainingText = Num(balance.Remaining);
            UnpaidText = Num(balance.UnpaidDays);
            BalanceCaption = _selectedRequest.EmployeeName + " — droit annuel " + Num(balance.Entitlement) + " jours";
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

            ShowEditor(new LeaveEditViewModel(_services, employees, null));
        }

        private void Edit()
        {
            IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
            ShowEditor(new LeaveEditViewModel(_services, employees, _selectedRequest.Request));
        }

        private void ShowEditor(LeaveEditViewModel vm)
        {
            var window = new LeaveEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                Load();
                StatusMessage = "Demande enregistrée.";
            }
        }

        private void Approve()
        {
            Run(_services.Leave.Approve(_selectedRequest.Id, null),
                "Congé approuvé — les jours sont enregistrés dans la présence.");
        }

        private void Reject()
        {
            Run(_services.Leave.Reject(_selectedRequest.Id, null), "Demande refusée.");
        }

        private void CancelRequest()
        {
            if (!Dialogs.Confirm("Annuler ce congé approuvé ? Les jours seront retirés de la présence."))
            {
                return;
            }

            Run(_services.Leave.Cancel(_selectedRequest.Id, null),
                "Congé annulé — les jours ont été retirés de la présence.");
        }

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer définitivement cette demande ?"))
            {
                return;
            }

            Run(_services.Leave.Delete(_selectedRequest.Id), "Demande supprimée.");
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

        private void OpenSettings()
        {
            var vm = new LeaveSettingsViewModel(_services.Leave);
            var window = new LeaveSettingsWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                // Balances and day counts depend on these values — reload.
                Load();
                StatusMessage = "Paramètres enregistrés.";
            }
        }

        private void OpenBalances()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info("Sélectionnez d'abord une entreprise.");
                return;
            }

            var vm = new LeaveBalancesViewModel(_services, _selectedCompany, _selectedYear);
            var window = new LeaveBalancesWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
        }

        private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
