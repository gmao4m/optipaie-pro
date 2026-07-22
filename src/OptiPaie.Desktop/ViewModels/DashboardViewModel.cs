using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The executive dashboard: a role-agnostic company-wide overview aggregated from
    /// every HR module — KPIs, a single "à traiter" (approvals) queue and an upcoming
    /// deadlines widget — with one-click navigation to the relevant module. Read-only;
    /// it never touches payroll.
    /// </summary>
    public sealed class DashboardViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly Action<string> _navigate;

        private string _employees = "0", _activeContracts = "0", _expiring = "0", _pendingLeave = "0";
        private string _activeLoans = "0", _loanOutstanding = "0", _presentToday = "0", _onLeave = "0", _onMission = "0";
        private string _openPostings = "0", _candidates = "0", _assetsAssigned = "0", _trainingUpcoming = "0", _companies = "0";
        private string _dateLabel = string.Empty;
        private string _approvalsHeader = "À traiter", _deadlinesHeader = "Échéances à venir";

        public DashboardViewModel(AppServices services, Action<string> navigate)
        {
            _services = services;
            _navigate = navigate;

            RefreshCommand = new RelayCommand(Load);
            OpenCommand = new RelayCommand(p => Open(p as string));
            NewPayrollCommand = new RelayCommand(() => _navigate("payroll"));
            NewEmployeeCommand = new RelayCommand(() => _navigate("employees"));
            AttendanceCommand = new RelayCommand(() => _navigate("attendance"));
            LeaveCommand = new RelayCommand(() => _navigate("leave"));
        }

        public string Greeting => "Tableau de bord";
        public string DateLabel { get => _dateLabel; private set => Set(ref _dateLabel, value); }

        public string Companies { get => _companies; private set => Set(ref _companies, value); }
        public string Employees { get => _employees; private set => Set(ref _employees, value); }
        public string ActiveContracts { get => _activeContracts; private set => Set(ref _activeContracts, value); }
        public string Expiring { get => _expiring; private set => Set(ref _expiring, value); }
        public string PendingLeave { get => _pendingLeave; private set => Set(ref _pendingLeave, value); }
        public string ActiveLoans { get => _activeLoans; private set => Set(ref _activeLoans, value); }
        public string LoanOutstanding { get => _loanOutstanding; private set => Set(ref _loanOutstanding, value); }
        public string PresentToday { get => _presentToday; private set => Set(ref _presentToday, value); }
        public string OnLeave { get => _onLeave; private set => Set(ref _onLeave, value); }
        public string OnMission { get => _onMission; private set => Set(ref _onMission, value); }
        public string OpenPostings { get => _openPostings; private set => Set(ref _openPostings, value); }
        public string Candidates { get => _candidates; private set => Set(ref _candidates, value); }
        public string AssetsAssigned { get => _assetsAssigned; private set => Set(ref _assetsAssigned, value); }
        public string TrainingUpcoming { get => _trainingUpcoming; private set => Set(ref _trainingUpcoming, value); }

        public string ApprovalsHeader { get => _approvalsHeader; private set => Set(ref _approvalsHeader, value); }
        public string DeadlinesHeader { get => _deadlinesHeader; private set => Set(ref _deadlinesHeader, value); }

        public ObservableCollection<ApprovalItem> Approvals { get; } = new ObservableCollection<ApprovalItem>();
        public ObservableCollection<DeadlineItem> Deadlines { get; } = new ObservableCollection<DeadlineItem>();
        public ObservableCollection<ActivityLine> RecentActivity { get; } = new ObservableCollection<ActivityLine>();

        public bool HasApprovals => Approvals.Count > 0;
        public bool HasDeadlines => Deadlines.Count > 0;
        public bool HasActivity => RecentActivity.Count > 0;

        public ICommand RefreshCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand NewPayrollCommand { get; }
        public ICommand NewEmployeeCommand { get; }
        public ICommand AttendanceCommand { get; }
        public ICommand LeaveCommand { get; }

        public void OnActivated() => Load();

        private void Load()
        {
            DateLabel = Capitalize(DateTime.Now.ToString("dddd d MMMM yyyy", Fr));

            DashboardSnapshot s = _services.Dashboard.Build(30);

            Companies = s.Companies.ToString();
            Employees = s.Employees.ToString();
            ActiveContracts = s.ActiveContracts.ToString();
            Expiring = s.ContractsExpiringSoon.ToString();
            PendingLeave = s.PendingLeave.ToString();
            ActiveLoans = s.ActiveLoans.ToString();
            LoanOutstanding = s.LoanOutstanding.ToString("N0", Fr);
            PresentToday = s.PresentToday.ToString();
            OnLeave = s.OnLeaveToday.ToString();
            OnMission = s.OnMissionToday.ToString();
            OpenPostings = s.OpenPostings.ToString();
            Candidates = s.Candidates.ToString();
            AssetsAssigned = s.AssetsAssigned.ToString();
            TrainingUpcoming = s.TrainingUpcoming.ToString();

            Approvals.Clear();
            foreach (ApprovalItem a in s.Approvals) Approvals.Add(a);
            Deadlines.Clear();
            foreach (DeadlineItem d in s.Deadlines) Deadlines.Add(d);

            RecentActivity.Clear();
            foreach (Core.Entities.AuditEntry e in _services.Audit.GetRecent(12))
            {
                RecentActivity.Add(new ActivityLine
                {
                    Text = e.Summary ?? e.Action.ToString(),
                    Detail = (string.IsNullOrEmpty(e.NewValue) ? string.Empty : e.OldValue + " → " + e.NewValue + " · ") +
                             e.CreatedAtUtc.ToLocalTime().ToString("dd/MM HH:mm", Fr) +
                             (string.IsNullOrWhiteSpace(e.Actor) ? string.Empty : " · " + e.Actor)
                });
            }

            ApprovalsHeader = "À traiter" + (Approvals.Count > 0 ? " (" + Approvals.Count + ")" : string.Empty);
            DeadlinesHeader = "Échéances à venir" + (Deadlines.Count > 0 ? " (" + Deadlines.Count + ")" : string.Empty);
            Raise(nameof(HasApprovals));
            Raise(nameof(HasDeadlines));
            Raise(nameof(HasActivity));
        }

        private void Open(string moduleKey)
        {
            if (!string.IsNullOrWhiteSpace(moduleKey)) _navigate(moduleKey);
        }

        private static string Capitalize(string s)
        {
            return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Fr) + s.Substring(1);
        }
    }

    /// <summary>One line of the dashboard activity journal (from the audit trail).</summary>
    public sealed class ActivityLine
    {
        public string Text { get; set; }
        public string Detail { get; set; }
    }
}
