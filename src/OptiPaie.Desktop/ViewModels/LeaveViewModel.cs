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
using OptiPaie.Desktop.Localization;
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
        public long EmployeeId => Request.EmployeeId;
        public string EmployeeName { get; }
        public string TypeLabel => LeaveLabels.Type(Request.Type);
        public bool IsDraft => Request.IsDraft;

        /// <summary>A draft is shown as "Brouillon" even though it is stored at Status=Pending.</summary>
        public string StatusLabel => IsDraft
            ? TranslationSource.Instance["Enum_LeaveStatus_Draft"]
            : LeaveLabels.Status(Request.Status);

        public string Period =>
            Request.StartDate.ToString("dd/MM/yyyy") + " → " + Request.EndDate.ToString("dd/MM/yyyy");
        public string DaysText => Request.Days.ToString("0.##", CultureInfo.InvariantCulture);
        public string Reason => Request.Reason;

        // --- state predicates: only the transitions valid in the current state ---
        /// <summary>Live (submitted, undecided) — the only state that can be approved/refused/returned.</summary>
        public bool IsLivePending => Request.Status == LeaveStatus.Pending && !IsDraft;
        public bool IsApproved => Request.Status == LeaveStatus.Approved;

        public bool CanSubmit => IsDraft;                                   // Brouillon → En attente
        public bool CanApprove => IsLivePending;
        public bool CanReject => IsLivePending;
        public bool CanReturn => IsLivePending;                            // En attente → Brouillon
        public bool CanCancel => IsApproved;                              // Approuvée → Annulée
        public bool CanEdit => Request.Status == LeaveStatus.Pending;      // editable while draft or pending
        public bool CanDelete => true;

        /// <summary>Semantic colour bucket for the status pill (shared with the other lists).</summary>
        public string StatusKind
        {
            get
            {
                if (IsDraft) return "neutral"; // a draft has not entered the decision flow yet
                switch (Request.Status)
                {
                    case LeaveStatus.Approved: return "success"; // granted
                    case LeaveStatus.Pending: return "pending";  // awaiting decision
                    case LeaveStatus.Rejected: return "danger";  // refused
                    default: return "neutral";                   // cancelled
                }
            }
        }
    }

    /// <summary>Localized labels for the leave enums (resolved for the active language).</summary>
    public static class LeaveLabels
    {
        private static string L(string key) => TranslationSource.Instance[key];

        public static string Type(LeaveType type)
        {
            switch (type)
            {
                case LeaveType.Annual: return L("Enum_LeaveType_Annual");
                case LeaveType.Sick: return L("Enum_LeaveType_Sick");
                case LeaveType.Unpaid: return L("Enum_LeaveType_Unpaid");
                case LeaveType.Maternity: return L("Enum_LeaveType_Maternity");
                case LeaveType.Special: return L("Enum_LeaveType_Special");
                default: return L("Enum_LeaveType_Other");
            }
        }

        public static string Status(LeaveStatus status)
        {
            switch (status)
            {
                case LeaveStatus.Pending: return L("Enum_LeaveStatus_Pending");
                case LeaveStatus.Approved: return L("Enum_LeaveStatus_Approved");
                case LeaveStatus.Rejected: return L("Enum_LeaveStatus_Rejected");
                case LeaveStatus.Cancelled: return L("Enum_LeaveStatus_Cancelled");
                default: return string.Empty;
            }
        }
    }

    /// <summary>A leave type with its localized label (for combo boxes).</summary>
    public sealed class LeaveTypeOption
    {
        public LeaveTypeOption(LeaveType value) { Value = value; Label = LeaveLabels.Type(value); }
        public LeaveType Value { get; }
        public string Label { get; }
    }

    /// <summary>A status bucket for the list filter.</summary>
    public sealed class LeaveStatusFilter
    {
        public LeaveStatusFilter(string key, string label) { Key = key; Label = label; }
        public string Key { get; }
        public string Label { get; }
    }

    public sealed class EmployeeFilterOption
    {
        public EmployeeFilterOption(long id, string name) { Id = id; Name = name; }
        public long Id { get; }
        public string Name { get; }
    }

    public sealed class MonthFilterOption
    {
        public MonthFilterOption(int month, string label) { Month = month; Label = label; }
        public int Month { get; } // 0 = all
        public string Label { get; }
    }

    /// <summary>
    /// Congés — requests of one company for one year, filtered by status/employee/period,
    /// with the annual-leave position of the selected employee. Only the transitions valid in
    /// the current state are offered, and approving writes the days straight into Attendance.
    /// </summary>
    public sealed class LeaveViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;
        private readonly Dictionary<long, string> _employeeNames = new Dictionary<long, string>();
        private readonly List<LeaveRowViewModel> _allRows = new List<LeaveRowViewModel>();

        private Company _selectedCompany;
        private int _selectedYear = DateTime.Today.Year;
        private LeaveRowViewModel _selectedRequest;
        private LeaveStatusFilter _selectedStatusFilter;
        private EmployeeFilterOption _selectedEmployeeFilter;
        private MonthFilterOption _selectedMonthFilter;
        private string _pendingText = "0", _takenText = "0", _availableText = "0", _unpaidText = "0";
        private string _balanceCaption = string.Empty;
        private string _statusMessage = string.Empty;

        public LeaveViewModel(AppServices services)
        {
            _services = services;

            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);

            BuildStatusFilters();
            BuildMonthFilters();
            _selectedStatusFilter = StatusFilters[0];
            _selectedMonthFilter = MonthFilters[0];

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selectedRequest != null && _selectedRequest.CanEdit);
            SubmitCommand = new RelayCommand(Submit, () => _selectedRequest != null && _selectedRequest.CanSubmit);
            ApproveCommand = new RelayCommand(Approve, () => _selectedRequest != null && _selectedRequest.CanApprove);
            RejectCommand = new RelayCommand(Reject, () => _selectedRequest != null && _selectedRequest.CanReject);
            ReturnCommand = new RelayCommand(ReturnToDraft, () => _selectedRequest != null && _selectedRequest.CanReturn);
            CancelCommand = new RelayCommand(CancelRequest, () => _selectedRequest != null && _selectedRequest.CanCancel);
            DeleteCommand = new RelayCommand(Delete, () => _selectedRequest != null);
            BalancesCommand = new RelayCommand(OpenBalances);
            SettingsCommand = new RelayCommand(OpenSettings);
        }

        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<LeaveRowViewModel> Requests { get; } = new ObservableCollection<LeaveRowViewModel>();
        public ObservableCollection<LeaveStatusFilter> StatusFilters { get; } = new ObservableCollection<LeaveStatusFilter>();
        public ObservableCollection<EmployeeFilterOption> EmployeeFilters { get; } = new ObservableCollection<EmployeeFilterOption>();
        public ObservableCollection<MonthFilterOption> MonthFilters { get; } = new ObservableCollection<MonthFilterOption>();

        public int SelectedYear
        {
            get => _selectedYear;
            set { if (Set(ref _selectedYear, value)) Load(); }
        }

        public LeaveStatusFilter SelectedStatusFilter
        {
            get => _selectedStatusFilter;
            set { if (Set(ref _selectedStatusFilter, value)) ApplyFilter(); }
        }

        public EmployeeFilterOption SelectedEmployeeFilter
        {
            get => _selectedEmployeeFilter;
            set { if (Set(ref _selectedEmployeeFilter, value)) ApplyFilter(); }
        }

        public MonthFilterOption SelectedMonthFilter
        {
            get => _selectedMonthFilter;
            set { if (Set(ref _selectedMonthFilter, value)) ApplyFilter(); }
        }

        public LeaveRowViewModel SelectedRequest
        {
            get => _selectedRequest;
            set
            {
                if (!Set(ref _selectedRequest, value)) return;
                UpdateBalance();
                RaiseActionFlags();
            }
        }

        public string PendingText { get => _pendingText; private set => Set(ref _pendingText, value); }
        public string TakenText { get => _takenText; private set => Set(ref _takenText, value); }
        public string AvailableText { get => _availableText; private set => Set(ref _availableText, value); }
        public string UnpaidText { get => _unpaidText; private set => Set(ref _unpaidText, value); }
        public string BalanceCaption { get => _balanceCaption; private set => Set(ref _balanceCaption, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        // Contextual availability of each action, for the current selection (drive button visibility).
        public bool CanSubmit => _selectedRequest != null && _selectedRequest.CanSubmit;
        public bool CanApprove => _selectedRequest != null && _selectedRequest.CanApprove;
        public bool CanReject => _selectedRequest != null && _selectedRequest.CanReject;
        public bool CanReturn => _selectedRequest != null && _selectedRequest.CanReturn;
        public bool CanCancel => _selectedRequest != null && _selectedRequest.CanCancel;
        public bool CanEdit => _selectedRequest != null && _selectedRequest.CanEdit;

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand SubmitCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RejectCommand { get; }
        public ICommand ReturnCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand BalancesCommand { get; }
        public ICommand SettingsCommand { get; }

        public void OnActivated()
        {
            _selectedCompany = _services.CompanyContext.Active;
            Load();
        }

        private void Load()
        {
            _allRows.Clear();
            _employeeNames.Clear();

            if (_selectedCompany == null)
            {
                ApplyFilter();
                return;
            }

            foreach (Employee employee in _services.Employees.GetByCompany(_selectedCompany.Id))
            {
                _employeeNames[employee.Id] = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
            }

            foreach (LeaveRequest request in _services.Leave.GetByCompanyYear(_selectedCompany.Id, _selectedYear))
            {
                _employeeNames.TryGetValue(request.EmployeeId, out string name);
                _allRows.Add(new LeaveRowViewModel(request, name ?? "—"));
            }

            RebuildEmployeeFilter();
            ApplyFilter();
        }

        /// <summary>Applies the status/employee/period filters to the loaded rows.</summary>
        private void ApplyFilter()
        {
            Requests.Clear();

            IEnumerable<LeaveRowViewModel> rows = _allRows;

            string statusKey = _selectedStatusFilter?.Key ?? "all";
            if (statusKey != "all") rows = rows.Where(r => MatchesStatus(r, statusKey));

            long employeeId = _selectedEmployeeFilter?.Id ?? 0;
            if (employeeId > 0) rows = rows.Where(r => r.EmployeeId == employeeId);

            int month = _selectedMonthFilter?.Month ?? 0;
            if (month > 0) rows = rows.Where(r => r.Request.StartDate.Month <= month && r.Request.EndDate.Month >= month);

            foreach (LeaveRowViewModel row in rows) Requests.Add(row);

            SelectedRequest = Requests.FirstOrDefault();

            int pending = _allRows.Count(r => r.IsLivePending);
            StatusMessage = Requests.Count + " / " + _allRows.Count + " demande(s) · " + pending + " en attente";
            UpdateBalance();
        }

        private static bool MatchesStatus(LeaveRowViewModel r, string key)
        {
            switch (key)
            {
                case "draft": return r.IsDraft;
                case "pending": return r.IsLivePending;
                case "approved": return r.Request.Status == LeaveStatus.Approved;
                case "rejected": return r.Request.Status == LeaveStatus.Rejected;
                case "cancelled": return r.Request.Status == LeaveStatus.Cancelled;
                default: return true;
            }
        }

        /// <summary>Employee fiche: the annual-leave position (acquis / consommé / réservé / disponible) of the selected employee.</summary>
        private void UpdateBalance()
        {
            if (_selectedRequest == null)
            {
                PendingText = TakenText = AvailableText = UnpaidText = "0";
                BalanceCaption = "Sélectionnez une demande pour voir le solde de l'employé.";
                return;
            }

            LeaveBalance balance = _services.Leave.GetBalance(_selectedRequest.Request.EmployeeId, _selectedYear);

            PendingText = Num(balance.Pending);
            TakenText = Num(balance.Taken);
            AvailableText = Num(balance.Available);
            UnpaidText = Num(balance.UnpaidDays);
            BalanceCaption = _selectedRequest.EmployeeName + " — droit annuel " + Num(balance.Entitlement) + " jours";
        }

        private void RaiseActionFlags()
        {
            Raise(nameof(CanSubmit));
            Raise(nameof(CanApprove));
            Raise(nameof(CanReject));
            Raise(nameof(CanReturn));
            Raise(nameof(CanCancel));
            Raise(nameof(CanEdit));
            CommandManager.InvalidateRequerySuggested();
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
            if (_selectedRequest == null) return;
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

        private void Submit()
        {
            if (_selectedRequest == null) return;
            Run(_services.Leave.Submit(_selectedRequest.Id), "Demande soumise pour décision.");
        }

        private void Approve()
        {
            if (_selectedRequest == null) return;
            if (!Dialogs.Confirm(L("Leave_ConfirmApprove"))) return;

            Run(_services.Leave.Approve(_selectedRequest.Id, null),
                "Congé approuvé — les jours sont enregistrés dans la présence.");
        }

        private void Reject()
        {
            if (_selectedRequest == null) return;
            // The mandatory motive prompt is itself the confirmation step for a refusal.
            string motif = Dialogs.Prompt(L("Leave_ActReject"), L("Leave_MotifReject"), null, required: true);
            if (motif == null) return;

            Run(_services.Leave.Reject(_selectedRequest.Id, motif), "Demande refusée.");
        }

        private void ReturnToDraft()
        {
            if (_selectedRequest == null) return;
            string motif = Dialogs.Prompt(L("Leave_ActReturn"), L("Leave_MotifReturn"), null, required: false);
            if (motif == null) return; // cancelled

            Run(_services.Leave.ReturnToDraft(_selectedRequest.Id, motif), "Demande renvoyée en brouillon.");
        }

        private void CancelRequest()
        {
            if (_selectedRequest == null) return;
            string motif = Dialogs.Prompt(L("Leave_ActCancel"), L("Leave_MotifCancel"), null, required: true);
            if (motif == null) return;

            Run(_services.Leave.Cancel(_selectedRequest.Id, motif),
                "Congé annulé — les jours ont été retirés de la présence.");
        }

        private void Delete()
        {
            if (_selectedRequest == null) return;
            if (!Dialogs.Confirm("Supprimer définitivement cette demande ?")) return;

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

        private void BuildStatusFilters()
        {
            StatusFilters.Add(new LeaveStatusFilter("all", L("Common_All")));
            StatusFilters.Add(new LeaveStatusFilter("draft", L("Enum_LeaveStatus_Draft")));
            StatusFilters.Add(new LeaveStatusFilter("pending", L("Enum_LeaveStatus_Pending")));
            StatusFilters.Add(new LeaveStatusFilter("approved", L("Enum_LeaveStatus_Approved")));
            StatusFilters.Add(new LeaveStatusFilter("rejected", L("Enum_LeaveStatus_Rejected")));
            StatusFilters.Add(new LeaveStatusFilter("cancelled", L("Enum_LeaveStatus_Cancelled")));
        }

        private void BuildMonthFilters()
        {
            MonthFilters.Add(new MonthFilterOption(0, L("Common_All")));
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            for (int m = 1; m <= 12; m++)
            {
                MonthFilters.Add(new MonthFilterOption(m, fr.DateTimeFormat.GetMonthName(m)));
            }
        }

        private void RebuildEmployeeFilter()
        {
            long previous = _selectedEmployeeFilter?.Id ?? 0;
            EmployeeFilters.Clear();
            EmployeeFilters.Add(new EmployeeFilterOption(0, L("Common_All")));
            foreach (KeyValuePair<long, string> pair in _employeeNames.OrderBy(p => p.Value))
            {
                EmployeeFilters.Add(new EmployeeFilterOption(pair.Key, pair.Value));
            }

            _selectedEmployeeFilter = EmployeeFilters.FirstOrDefault(e => e.Id == previous) ?? EmployeeFilters[0];
            Raise(nameof(SelectedEmployeeFilter));
        }

        private static string L(string key) => TranslationSource.Instance[key];
        private static string Num(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
