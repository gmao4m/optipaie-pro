using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
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
        private string _masseSalariale = "0", _salaireMoyen = "0";
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

        public string Greeting => _services.Localization.GetString("Shell_Nav_Dashboard");
        public string DateLabel { get => _dateLabel; private set => Set(ref _dateLabel, value); }

        private string L(string key) => _services.Localization.GetString(key);

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

        /// <summary>Total monthly base-salary mass (masse salariale) of the active company.</summary>
        public string MasseSalariale { get => _masseSalariale; private set => Set(ref _masseSalariale, value); }

        /// <summary>Average base salary across the active company's employees.</summary>
        public string SalaireMoyen { get => _salaireMoyen; private set => Set(ref _salaireMoyen, value); }

        /// <summary>Masse salariale over the last 6 months (bar chart).</summary>
        public ObservableCollection<SalaryBar> SalaryTrend { get; } = new ObservableCollection<SalaryBar>();

        /// <summary>Employee distribution by department (donut chart + legend).</summary>
        public ObservableCollection<DeptSlice> DeptSlices { get; } = new ObservableCollection<DeptSlice>();

        public bool HasChartData => DeptSlices.Count > 0;

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
            Raise(nameof(Greeting));
            CultureInfo culture = _services.Localization.CurrentCulture;
            DateLabel = Capitalize(DateTime.Now.ToString("dddd d MMMM yyyy", culture));

            // The dashboard is strictly scoped to the active company (never all companies).
            long companyId = _services.CompanyContext.ActiveId;
            if (companyId <= 0)
            {
                // No active company yet — show nothing rather than a cross-company or zero total.
                return;
            }

            DashboardSnapshot s = _services.Dashboard.Build(companyId, 30);

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

            ApprovalsHeader = L("Dashboard_Approvals") + (Approvals.Count > 0 ? " (" + Approvals.Count + ")" : string.Empty);
            DeadlinesHeader = L("Dashboard_Deadlines") + (Deadlines.Count > 0 ? " (" + Deadlines.Count + ")" : string.Empty);
            Raise(nameof(HasApprovals));
            Raise(nameof(HasDeadlines));
            Raise(nameof(HasActivity));

            BuildSalaryWidgets();
        }

        /// <summary>
        /// Computes the payroll KPIs and the two charts (masse salariale trend + department
        /// distribution) for the active company. Read-only aggregation of base salaries — it
        /// never runs the payroll engine.
        /// </summary>
        private void BuildSalaryWidgets()
        {
            SalaryTrend.Clear();
            DeptSlices.Clear();

            long companyId = _services.CompanyContext.ActiveId;
            if (companyId <= 0)
            {
                MasseSalariale = SalaireMoyen = "0";
                Raise(nameof(HasChartData));
                return;
            }

            IReadOnlyList<Employee> active = _services.Employees.GetByCompany(companyId, false);
            decimal masse = active.Sum(e => e.BaseSalary);
            MasseSalariale = FormatDa(masse);
            SalaireMoyen = FormatDa(active.Count > 0 ? Math.Round(masse / active.Count) : 0m);

            // Masse salariale over the last 6 months, reconstructed from hire/exit dates (the
            // roster including leavers, so a month reflects who was actually employed then).
            IReadOnlyList<Employee> roster = _services.Employees.GetByCompany(companyId, true);
            CultureInfo culture = _services.Localization.CurrentCulture;
            DateTime firstThisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var monthly = new List<decimal>();
            var labels = new List<string>();
            for (int i = 5; i >= 0; i--)
            {
                DateTime start = firstThisMonth.AddMonths(-i);
                DateTime end = start.AddMonths(1).AddDays(-1);
                decimal amt = roster.Where(e => e.HireDate.Date <= end && (e.ExitDate == null || e.ExitDate.Value.Date >= start))
                                    .Sum(e => e.BaseSalary);
                monthly.Add(amt);
                labels.Add(Capitalize(start.ToString("MMM", culture)));
            }

            decimal peak = monthly.Count > 0 ? monthly.Max() : 0m;
            decimal trough = monthly.Count > 0 ? monthly.Min() : 0m;
            if (peak <= 0m) peak = 1m;
            Brush barBrush = Res("Brand", Color.FromRgb(0x0E, 0x9F, 0x6E));
            for (int i = 0; i < monthly.Count; i++)
            {
                // Normalise between the period's min and max so a small but real trend is
                // legible; the exact amount is printed on every bar, so this only scales height.
                double frac = peak > trough ? (double)((monthly[i] - trough) / (peak - trough)) : 1.0;
                SalaryTrend.Add(new SalaryBar
                {
                    MonthLabel = labels[i],
                    HeightPx = 62.0 + frac * 90.0,
                    AmountText = (monthly[i] / 1000m).ToString("N0", Fr) + " K",
                    Fill = barBrush
                });
            }

            // Department distribution donut (largest slice first).
            var groups = active
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Department) ? "—" : e.Department.Trim())
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            int total = groups.Sum(g => g.Count);
            Brush[] palette =
            {
                Res("Brand", Color.FromRgb(0x0E, 0x9F, 0x6E)),
                Res("Accent", Color.FromRgb(0xE3, 0xB3, 0x41)),
                Res("Salary", Color.FromRgb(0x0E, 0x9F, 0x6E)),
                Res("Warning", Color.FromRgb(0xC0, 0x8A, 0x2E)),
                Res("Employer", Color.FromRgb(0x6C, 0x87, 0xEC)),
                Res("Deduction", Color.FromRgb(0xE5, 0x48, 0x4D))
            };

            double angle = -90.0; // start at 12 o'clock
            int ci = 0;
            foreach (var g in groups)
            {
                double frac = total > 0 ? (double)g.Count / total : 0.0;
                double sweep = frac * 360.0;
                if (sweep >= 359.9) sweep = 359.9; // a single full ring can't be one arc
                DeptSlices.Add(new DeptSlice
                {
                    Name = g.Name,
                    Count = g.Count,
                    CountText = g.Count.ToString(),
                    PercentText = Math.Round(frac * 100).ToString("0", Fr) + "%",
                    Fill = palette[ci % palette.Length],
                    PathData = DonutArc(96, 96, 84, 52, angle, angle + sweep)
                });
                angle += sweep;
                ci++;
            }

            Raise(nameof(HasChartData));
        }

        private string FormatDa(decimal amount) => amount.ToString("N0", Fr) + " DA";

        /// <summary>Resolves a themed brush by resource key, falling back to a fixed colour headlessly.</summary>
        private static Brush Res(string key, Color fallback)
        {
            Brush b = Application.Current != null ? Application.Current.TryFindResource(key) as Brush : null;
            if (b != null) return b;
            var solid = new SolidColorBrush(fallback);
            solid.Freeze();
            return solid;
        }

        /// <summary>Builds the SVG-style path for one donut ring segment (angles in degrees, clockwise from top).</summary>
        private static string DonutArc(double cx, double cy, double outerR, double innerR, double a0, double a1)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            double r0 = a0 * Math.PI / 180.0, r1 = a1 * Math.PI / 180.0;
            double ox0 = cx + outerR * Math.Cos(r0), oy0 = cy + outerR * Math.Sin(r0);
            double ox1 = cx + outerR * Math.Cos(r1), oy1 = cy + outerR * Math.Sin(r1);
            double ix1 = cx + innerR * Math.Cos(r1), iy1 = cy + innerR * Math.Sin(r1);
            double ix0 = cx + innerR * Math.Cos(r0), iy0 = cy + innerR * Math.Sin(r0);
            int large = (a1 - a0) > 180.0 ? 1 : 0;
            return string.Format(ci,
                "M {0:0.##},{1:0.##} A {2:0.##},{2:0.##} 0 {3} 1 {4:0.##},{5:0.##} L {6:0.##},{7:0.##} A {8:0.##},{8:0.##} 0 {3} 0 {9:0.##},{10:0.##} Z",
                ox0, oy0, outerR, large, ox1, oy1, ix1, iy1, innerR, ix0, iy0);
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

    /// <summary>One bar of the masse-salariale trend chart.</summary>
    public sealed class SalaryBar
    {
        public string MonthLabel { get; set; }
        public double HeightPx { get; set; }
        public string AmountText { get; set; }
        public Brush Fill { get; set; }
    }

    /// <summary>One department segment of the distribution donut (+ its legend row).</summary>
    public sealed class DeptSlice
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public string CountText { get; set; }
        public string PercentText { get; set; }
        public string PathData { get; set; }
        public Brush Fill { get; set; }
    }
}
