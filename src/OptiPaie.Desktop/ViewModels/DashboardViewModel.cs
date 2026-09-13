using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Core.Dtos;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The executive dashboard: a role-agnostic, company-scoped overview built in ONE off-thread
    /// call (<see cref="Core.Interfaces.Services.IDashboardService.BuildOverview"/>) so the UI never
    /// freezes. It leads with a few headline figures and the two things a manager can act on
    /// (approvals + deadlines), then the workforce analytics and payroll trend, then a calm strip of
    /// secondary indicators. Read-only; it never touches payroll. Every displayed string is localized,
    /// so the whole screen switches fully between French and Arabic.
    /// </summary>
    public sealed class DashboardViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly Action<string> _navigate;
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

        private bool _isLoading, _hasData, _noCompany;
        private PeriodOption _period;
        private string _dateLabel = string.Empty;
        private string _masse = "—", _employees = "—", _presentToday = "—", _pendingCount = "0", _deadlineCount = "0", _presentSubtext = string.Empty;
        private string _salaireMoyen = "—", _activeContracts = "0", _onLeave = "0", _onMission = "0", _loanOutstanding = "0";
        private string _openPostings = "0", _candidates = "0", _assetsAssigned = "0", _trainingUpcoming = "0";
        private string _approvalsHeader = string.Empty, _deadlinesHeader = string.Empty;
        private string _masseTrendCaption = string.Empty, _masseRangeText = string.Empty;
        private PointCollection _masseTrendPoints = new PointCollection();

        public DashboardViewModel(AppServices services, Action<string> navigate)
        {
            _services = services;
            _navigate = navigate;
            Workforce = new WorkforceViewModel(services);

            Periods.Add(new PeriodOption("month", L("Workforce_Period_Month")));
            Periods.Add(new PeriodOption("quarter", L("Workforce_Period_Quarter")));
            Periods.Add(new PeriodOption("year", L("Workforce_Period_Year")));
            _period = Periods[0];

            RefreshCommand = new RelayCommand(() => Reload(useCache: false));
            OpenCommand = new RelayCommand(p => Open(p as string));
            NewPayrollCommand = new RelayCommand(() => _navigate("payroll"));
            NewEmployeeCommand = new RelayCommand(() => _navigate("employees"));
            AttendanceCommand = new RelayCommand(() => _navigate("attendance"));
            LeaveCommand = new RelayCommand(() => _navigate("leave"));
        }

        // ── header ──
        public string Greeting => _services.Localization.GetString("Shell_Nav_Dashboard");
        public string DateLabel { get => _dateLabel; private set => Set(ref _dateLabel, value); }

        // ── loading / empty state ──
        public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) Raise(nameof(IsContentVisible)); } }
        public bool NoCompany { get => _noCompany; private set { if (Set(ref _noCompany, value)) Raise(nameof(IsContentVisible)); } }
        public bool IsContentVisible => !_isLoading && !_noCompany;

        // ── period ──
        public ObservableCollection<PeriodOption> Periods { get; } = new ObservableCollection<PeriodOption>();
        public PeriodOption SelectedPeriod
        {
            get => _period;
            set { if (Set(ref _period, value) && value != null && _hasData) Reload(useCache: true); }
        }

        // ── hero KPIs ──
        public string MasseSalariale { get => _masse; private set => Set(ref _masse, value); }
        public string Employees { get => _employees; private set => Set(ref _employees, value); }
        public string PresentToday { get => _presentToday; private set => Set(ref _presentToday, value); }
        /// <summary>Short explanation shown when "présents = 0" so a bare 0 next to a full roster
        /// never looks like a bug (e.g. no attendance entered today). Empty when the count speaks for itself.</summary>
        public string PresentSubtext { get => _presentSubtext; private set { if (Set(ref _presentSubtext, value)) Raise(nameof(HasPresentSubtext)); } }
        public bool HasPresentSubtext => !string.IsNullOrEmpty(_presentSubtext);
        public string PendingCount { get => _pendingCount; private set => Set(ref _pendingCount, value); }
        public string DeadlineCount { get => _deadlineCount; private set => Set(ref _deadlineCount, value); }

        /// <summary>Drills the headcount hero card to the nominative employee list.</summary>
        public ICommand OpenHeadcountCommand => Workforce.OpenHeadcountCommand;

        // ── secondary indicators ──
        public string SalaireMoyen { get => _salaireMoyen; private set => Set(ref _salaireMoyen, value); }
        public string ActiveContracts { get => _activeContracts; private set => Set(ref _activeContracts, value); }
        public string OnLeave { get => _onLeave; private set => Set(ref _onLeave, value); }
        public string OnMission { get => _onMission; private set => Set(ref _onMission, value); }
        public string LoanOutstanding { get => _loanOutstanding; private set => Set(ref _loanOutstanding, value); }
        public string OpenPostings { get => _openPostings; private set => Set(ref _openPostings, value); }
        public string Candidates { get => _candidates; private set => Set(ref _candidates, value); }
        public string AssetsAssigned { get => _assetsAssigned; private set => Set(ref _assetsAssigned, value); }
        public string TrainingUpcoming { get => _trainingUpcoming; private set => Set(ref _trainingUpcoming, value); }

        // ── payroll trend ──
        public PointCollection MasseTrendPoints { get => _masseTrendPoints; private set => Set(ref _masseTrendPoints, value); }
        public string MasseTrendCaption { get => _masseTrendCaption; private set => Set(ref _masseTrendCaption, value); }
        public string MasseRangeText { get => _masseRangeText; private set => Set(ref _masseRangeText, value); }

        // ── workforce analytics panel ──
        public WorkforceViewModel Workforce { get; }

        // ── action queues ──
        public string ApprovalsHeader { get => _approvalsHeader; private set => Set(ref _approvalsHeader, value); }
        public string DeadlinesHeader { get => _deadlinesHeader; private set => Set(ref _deadlinesHeader, value); }
        public ObservableCollection<QueueRow> Approvals { get; } = new ObservableCollection<QueueRow>();
        public ObservableCollection<QueueRow> Deadlines { get; } = new ObservableCollection<QueueRow>();
        public bool HasApprovals => Approvals.Count > 0;
        public bool HasDeadlines => Deadlines.Count > 0;

        // ── activity journal ──
        public ObservableCollection<ActivityLine> RecentActivity { get; } = new ObservableCollection<ActivityLine>();
        public bool HasActivity => RecentActivity.Count > 0;

        public ICommand RefreshCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand NewPayrollCommand { get; }
        public ICommand NewEmployeeCommand { get; }
        public ICommand AttendanceCommand { get; }
        public ICommand LeaveCommand { get; }

        public void OnActivated() => Reload(useCache: true);

        /// <summary>
        /// Builds and applies the overview SYNCHRONOUSLY on the calling thread. Production always uses
        /// the off-thread <see cref="OnActivated"/> path; this exists only for headless rendering and
        /// tests, where there is no dispatcher loop to marshal an async continuation back onto.
        /// </summary>
        public void LoadSynchronouslyForRender()
        {
            Raise(nameof(Greeting));
            DateLabel = Capitalize(DateTime.Now.ToString("dddd d MMMM yyyy", _services.Localization.CurrentCulture));

            long companyId = _services.CompanyContext.ActiveId;
            if (companyId <= 0) { NoCompany = true; IsLoading = false; return; }
            NoCompany = false;

            DateTime today = DateTime.Today;
            DashboardOverview ov = _services.Dashboard.BuildOverview(companyId, PeriodStart(_period?.Key, today), today, 30);
            var activity = _services.Audit.GetRecentForCompany(companyId, 12).ToList();
            ApplyOverview(ov, activity);
            IsLoading = false;
            _hasData = true;
        }

        private async void Reload(bool useCache)
        {
            Raise(nameof(Greeting));
            CultureInfo culture = _services.Localization.CurrentCulture;
            DateLabel = Capitalize(DateTime.Now.ToString("dddd d MMMM yyyy", culture));

            long companyId = _services.CompanyContext.ActiveId;
            if (companyId <= 0)
            {
                NoCompany = true;
                IsLoading = false;
                return;
            }
            NoCompany = false;

            DateTime today = DateTime.Today;
            DateTime start = PeriodStart(_period?.Key, today);
            string key = companyId + "|" + (_period?.Key ?? "month");

            // Show a cached snapshot instantly (no freeze, no skeleton) then refresh in the background;
            // otherwise show the skeleton while the first build runs.
            if (useCache && _cache.TryGetValue(key, out CacheEntry cached))
            {
                ApplyOverview(cached.Overview, cached.Activity);
                IsLoading = false;
            }
            else if (!_hasData)
            {
                IsLoading = true;
            }

            try
            {
                CacheEntry fresh = await Task.Run(() =>
                {
                    DashboardOverview ov = _services.Dashboard.BuildOverview(companyId, start, today, 30);
                    var activity = _services.Audit.GetRecentForCompany(companyId, 12).ToList();
                    return new CacheEntry { Overview = ov, Activity = activity };
                }).ConfigureAwait(true);

                _cache[key] = fresh;
                ApplyOverview(fresh.Overview, fresh.Activity);
                _hasData = true;
            }
            catch (Exception ex)
            {
                // Never let the dashboard take the app down: log and leave the last good content.
                CrashLog.Fatal("DashboardViewModel.Reload", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyOverview(DashboardOverview o, IReadOnlyList<Core.Entities.AuditEntry> activity)
        {
            // Hero.
            MasseSalariale = FormatDa(o.MasseSalariale);
            Employees = o.Workforce.Headcount.ToString(Fr);
            PresentToday = o.PresentToday.ToString(Fr);
            // A bare "0 présents" next to a full roster reads as a bug — explain it when nothing was entered.
            PresentSubtext = (o.PresentToday == 0 && !o.AttendanceRecordedToday && o.Workforce.Headcount > 0)
                ? L("Dashboard_NoAttendanceToday") : string.Empty;
            PendingCount = o.Approvals.Count.ToString(Fr);
            DeadlineCount = o.Deadlines.Count.ToString(Fr);

            // Secondary indicators.
            SalaireMoyen = FormatDa(o.SalaireMoyen);
            ActiveContracts = o.ActiveContracts.ToString(Fr);
            OnLeave = o.OnLeaveToday.ToString(Fr);
            OnMission = o.OnMissionToday.ToString(Fr);
            LoanOutstanding = FormatDa(o.LoanOutstanding);
            OpenPostings = o.OpenPostings.ToString(Fr);
            Candidates = o.Candidates.ToString(Fr);
            AssetsAssigned = o.AssetsAssigned.ToString(Fr);
            TrainingUpcoming = o.TrainingUpcoming.ToString(Fr);

            // Payroll trend (revealing scale + honest "stable" when flat).
            BuildTrend(o.MasseTrend);

            // Workforce analytics panel.
            Workforce.Apply(o.Workforce);

            // Approvals + deadlines (localized here — the DTOs are language-neutral).
            Approvals.Clear();
            foreach (ApprovalItem a in o.Approvals) Approvals.Add(BuildApproval(a));
            Deadlines.Clear();
            foreach (DeadlineItem d in o.Deadlines) Deadlines.Add(BuildDeadline(d));
            ApprovalsHeader = L("Dashboard_Approvals") + (Approvals.Count > 0 ? " (" + Approvals.Count + ")" : string.Empty);
            DeadlinesHeader = L("Dashboard_Deadlines") + (Deadlines.Count > 0 ? " (" + Deadlines.Count + ")" : string.Empty);
            Raise(nameof(HasApprovals));
            Raise(nameof(HasDeadlines));

            // Activity journal — collapse consecutive identical events (same summary, same minute,
            // same actor) into one row with a count, so a burst of identical edits reads as "×3"
            // instead of three noisy identical lines.
            RecentActivity.Clear();
            ActivityLine last = null;
            string lastKey = null;
            foreach (Core.Entities.AuditEntry e in activity ?? new List<Core.Entities.AuditEntry>())
            {
                DateTime local = e.CreatedAtUtc.ToLocalTime();
                string text = e.Summary ?? e.Action.ToString();
                string key = text + "|" + local.ToString("yyyyMMddHHmm", Fr) + "|" + (e.Actor ?? string.Empty);
                if (last != null && key == lastKey)
                {
                    last.Count++;
                    last.CountText = "×" + last.Count.ToString(Fr);
                    continue;
                }
                last = new ActivityLine
                {
                    Text = text,
                    Detail = (string.IsNullOrEmpty(e.NewValue) ? string.Empty : e.OldValue + " → " + e.NewValue + " · ") +
                             local.ToString("dd/MM HH:mm", Fr) +
                             (string.IsNullOrWhiteSpace(e.Actor) ? string.Empty : " · " + e.Actor),
                    Count = 1
                };
                lastKey = key;
                RecentActivity.Add(last);
            }
            Raise(nameof(HasActivity));
        }

        private QueueRow BuildApproval(ApprovalItem a)
        {
            string name = string.IsNullOrWhiteSpace(a.EmployeeName) ? "—" : a.EmployeeName;
            if (a.Kind == "recruitment")
            {
                return new QueueRow
                {
                    Title = string.Format(L("Dashboard_Approval_Interview"), name),
                    Detail = L("Dashboard_Approval_Candidate"),
                    ModuleKey = a.ModuleKey
                };
            }
            string detail = a.StartDate.HasValue && a.EndDate.HasValue
                ? a.StartDate.Value.ToString("dd/MM/yyyy", Fr) + " → " + a.EndDate.Value.ToString("dd/MM/yyyy", Fr)
                : string.Empty;
            return new QueueRow
            {
                Title = string.Format(L("Dashboard_Approval_Leave"), name),
                Detail = detail,
                ModuleKey = a.ModuleKey
            };
        }

        private QueueRow BuildDeadline(DeadlineItem d)
        {
            string name = string.IsNullOrWhiteSpace(d.EmployeeName) ? "—" : d.EmployeeName;
            string countdown = d.DaysLeft == 0
                ? L("Dashboard_Countdown_Today")
                : string.Format(L("Dashboard_Countdown_InDays"), d.DaysLeft);
            return new QueueRow
            {
                Title = string.Format(L("Dashboard_Deadline_ContractEnd"), name),
                Detail = string.Format(L("Dashboard_Deadline_OnDate"), d.Date.ToString("dd/MM/yyyy", Fr)) + "  ·  " + countdown,
                ModuleKey = d.ModuleKey
            };
        }

        private void BuildTrend(IReadOnlyList<MonthlyMass> trend)
        {
            var pts = new PointCollection();
            if (trend == null || trend.Count == 0) { MasseTrendPoints = pts; MasseTrendCaption = string.Empty; MasseRangeText = string.Empty; return; }

            List<decimal> vals = trend.Select(m => m.Amount).ToList();
            decimal min = vals.Min(), max = vals.Max(), first = vals.First(), last = vals.Last();
            double relRange = max > 0 ? (double)((max - min) / max) : 0.0;

            // "Effectively flat": draw a FLAT line and state it plainly — never amplify sub-1% noise
            // into a slope. Otherwise the line reflects the real series, and the caption describes the
            // WHOLE window (first→last) so the slope and the words always agree.
            bool flat = relRange < 0.01;
            const double w = 100.0, h = 30.0, pad = 3.0;
            double span = (double)(max - min);
            for (int i = 0; i < vals.Count; i++)
            {
                double x = vals.Count > 1 ? i / (double)(vals.Count - 1) * w : w / 2;
                double y = flat ? h / 2 : (h - pad) - (double)(vals[i] - min) / span * (h - 2 * pad);
                pts.Add(new System.Windows.Point(x, y));
            }
            MasseTrendPoints = pts;

            if (flat)
            {
                MasseTrendCaption = L("Dashboard_MasseStable");
            }
            else
            {
                decimal change = first > 0 ? Math.Round((last - first) / first * 100m, 1) : 0m;
                string arrow = change > 0 ? "▲" : change < 0 ? "▼" : "";
                MasseTrendCaption = (arrow + " " + Math.Abs(change).ToString("0.#", Fr) + " % " + L("Dashboard_MasseOver6Months")).Trim();
            }
            MasseRangeText = string.Format(L("Dashboard_MasseRange"), FormatDa(min), FormatDa(max));
        }

        private void Open(string moduleKey)
        {
            if (!string.IsNullOrWhiteSpace(moduleKey)) _navigate(moduleKey);
        }

        private string FormatDa(decimal amount) => amount.ToString("N0", Fr) + " " + L("Common_CurrencyDa");
        private string L(string key) => _services.Localization.GetString(key);

        private static DateTime PeriodStart(string key, DateTime today)
        {
            switch (key)
            {
                case "year": return new DateTime(today.Year, 1, 1);
                case "quarter":
                    int q = (today.Month - 1) / 3;
                    return new DateTime(today.Year, q * 3 + 1, 1);
                default: return new DateTime(today.Year, today.Month, 1);
            }
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Fr) + s.Substring(1);

        private sealed class CacheEntry
        {
            public DashboardOverview Overview { get; set; }
            public IReadOnlyList<Core.Entities.AuditEntry> Activity { get; set; }
        }
    }

    /// <summary>One localized, clickable row in the approvals or deadlines queue.</summary>
    public sealed class QueueRow
    {
        public string Title { get; set; }
        public string Detail { get; set; }
        public string ModuleKey { get; set; }
    }

    /// <summary>One line of the dashboard activity journal (from the audit trail). Consecutive
    /// identical events are folded into a single line carrying a <see cref="Count"/>.</summary>
    public sealed class ActivityLine
    {
        public string Text { get; set; }
        public string Detail { get; set; }
        public int Count { get; set; } = 1;
        /// <summary>"×N" when the same event repeated, else empty.</summary>
        public string CountText { get; set; } = string.Empty;
    }

    /// <summary>A period choice for the flow figures (entries/exits/turnover).</summary>
    public sealed class PeriodOption
    {
        public PeriodOption(string key, string label) { Key = key; Label = label; }
        public string Key { get; }
        public string Label { get; }
    }
}
