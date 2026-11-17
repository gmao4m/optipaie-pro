using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels.Attendance
{
    /// <summary>One status chip on a daily row: a coloured, one-click button for a status. The active
    /// one (the employee's current status that day) is highlighted; hovering shows its full name.</summary>
    public sealed class DailyChipViewModel : ObservableObject
    {
        private bool _isActive;

        public DailyChipViewModel(AttendanceStatus status, Action pick)
        {
            Status = status;
            Fill = AttendanceAppearance.Fill(status);
            Letter = AttendanceAppearance.Letter(status, false);
            Label = AttendanceAppearance.Label(status);
            PickCommand = new RelayCommand(pick);
        }

        public AttendanceStatus Status { get; }
        public Brush Fill { get; }
        public string Letter { get; }
        public string Label { get; }
        public ICommand PickCommand { get; }
        public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    }

    /// <summary>A read-only entry in the always-visible legend: coloured swatch + the status name in
    /// the interface language, so nobody has to guess what P / A / C / ½ mean.</summary>
    public sealed class LegendItemViewModel
    {
        public LegendItemViewModel(AttendanceStatus status)
        {
            Fill = AttendanceAppearance.Fill(status);
            Letter = AttendanceAppearance.Letter(status, false);
            Label = AttendanceAppearance.Label(status);
        }

        public Brush Fill { get; }
        public string Letter { get; }
        public string Label { get; }
    }

    /// <summary>One employee's row on the daily sheet. Everyone is Present by default; a single click
    /// on a chip sets a status and auto-saves. The four common statuses are immediate; the rarer ones
    /// sit behind a small "more" toggle so a line is not crowded with seven chips.</summary>
    public sealed class DailyRowViewModel : ObservableObject
    {
        private AttendanceStatus _status;
        private bool _moreOpen;

        public DailyRowViewModel(long employeeId, string name, string department, AttendanceStatus status,
            IReadOnlyList<AttendanceStatus> primary, IReadOnlyList<AttendanceStatus> more)
        {
            EmployeeId = employeeId;
            Name = name;
            Department = department;
            foreach (AttendanceStatus s in primary) PrimaryChips.Add(Chip(s));
            foreach (AttendanceStatus s in more) MoreChips.Add(Chip(s));
            ToggleMoreCommand = new RelayCommand(() => MoreOpen = !MoreOpen);
            SetStatus(status);
        }

        private DailyChipViewModel Chip(AttendanceStatus s)
        {
            AttendanceStatus captured = s;
            return new DailyChipViewModel(s, () => Picked?.Invoke(captured));
        }

        /// <summary>Raised when the user clicks a status chip on this row (the chosen status).</summary>
        public event Action<AttendanceStatus> Picked;

        public long EmployeeId { get; }
        public string Name { get; }
        public string Department { get; }
        public ObservableCollection<DailyChipViewModel> PrimaryChips { get; } = new ObservableCollection<DailyChipViewModel>();
        public ObservableCollection<DailyChipViewModel> MoreChips { get; } = new ObservableCollection<DailyChipViewModel>();
        public ICommand ToggleMoreCommand { get; }

        /// <summary>Whether the "more statuses" popup is open for this row.</summary>
        public bool MoreOpen { get => _moreOpen; set => Set(ref _moreOpen, value); }

        public AttendanceStatus Status
        {
            get => _status;
            private set { if (Set(ref _status, value)) { Raise(nameof(StatusLabel)); Raise(nameof(StatusFill)); } }
        }

        public string StatusLabel => AttendanceAppearance.Label(Status);
        public Brush StatusFill => AttendanceAppearance.Fill(Status);

        public void SetStatus(AttendanceStatus status)
        {
            Status = status;
            foreach (DailyChipViewModel c in PrimaryChips) c.IsActive = c.Status == status;
            foreach (DailyChipViewModel c in MoreChips) c.IsActive = c.Status == status;
        }
    }

    /// <summary>
    /// TODAY — the attendance entry screen. Opens on today, one row per employee EXPECTED that day,
    /// everyone Present by default; the user touches only the exceptions, one click, and it auto-saves.
    /// A live line shows how many are present / absent / on leave / half day, and an always-visible
    /// legend names every status. A rest day or holiday with nobody scheduled is announced plainly —
    /// but an employee whose own weekly pattern says he works that day (a guard on Friday) still shows,
    /// is expected present, and is counted and paid for it. Reads and writes the SAME records the month
    /// matrix and payroll use — nothing re-entered.
    /// </summary>
    public sealed class AttendanceDailyViewModel : ObservableObject
    {
        // The four common statuses are one click away on every row; the rarer three sit behind "more".
        private static readonly AttendanceStatus[] PrimaryStatuses =
        {
            AttendanceStatus.Present,
            AttendanceStatus.Absent,
            AttendanceStatus.Leave,
            AttendanceStatus.HalfDay
        };

        private static readonly AttendanceStatus[] MoreStatuses =
        {
            AttendanceStatus.JustifiedAbsence,
            AttendanceStatus.Late,
            AttendanceStatus.Mission
        };

        private readonly AppServices _services;
        private DateTime _date = DateTime.Today;
        private bool _isRestDay;
        private string _restDayText = string.Empty;
        private bool _hasCompany = true;
        private bool _hasEmployees;
        private string _statusMessage = string.Empty;
        private string _summaryLine = string.Empty;

        public AttendanceDailyViewModel(AppServices services)
        {
            _services = services;
            foreach (AttendanceStatus s in PrimaryStatuses.Concat(MoreStatuses))
                Legend.Add(new LegendItemViewModel(s));

            PrevDayCommand = new RelayCommand(() => Date = Date.AddDays(-1));
            NextDayCommand = new RelayCommand(() => Date = Date.AddDays(1));
            TodayCommand = new RelayCommand(() => Date = DateTime.Today);
        }

        public ObservableCollection<DailyRowViewModel> Rows { get; } = new ObservableCollection<DailyRowViewModel>();
        public ObservableCollection<LegendItemViewModel> Legend { get; } = new ObservableCollection<LegendItemViewModel>();

        public DateTime Date
        {
            get => _date;
            set { if (Set(ref _date, value.Date)) { Raise(nameof(DateText)); Load(); } }
        }

        /// <summary>The day, large and clear, in the active language.</summary>
        public string DateText
        {
            get
            {
                CultureInfo c = _services.Localization != null && _services.Localization.IsRightToLeft
                    ? CultureInfo.GetCultureInfo("ar-DZ")
                    : CultureInfo.GetCultureInfo("fr-FR");
                string t = Date.ToString("dddd d MMMM yyyy", c);
                return c.TextInfo.ToTitleCase(t);
            }
        }

        public bool IsRestDay { get => _isRestDay; private set => Set(ref _isRestDay, value); }
        public string RestDayText { get => _restDayText; private set => Set(ref _restDayText, value); }
        public bool HasCompany { get => _hasCompany; private set => Set(ref _hasCompany, value); }

        /// <summary>True when the company has at least one employee (independent of who is scheduled
        /// today). Distinguishes "no employees at all" (empty state) from "a rest day" (banner).</summary>
        public bool HasEmployees { get => _hasEmployees; private set => Set(ref _hasEmployees, value); }

        /// <summary>True when there is a list to show (at least one employee is expected today).</summary>
        public bool HasRows => Rows.Count > 0;

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public string SummaryLine { get => _summaryLine; private set => Set(ref _summaryLine, value); }

        public ICommand PrevDayCommand { get; }
        public ICommand NextDayCommand { get; }
        public ICommand TodayCommand { get; }

        private static string L(string key) => OptiPaie.Desktop.Localization.TranslationSource.Instance[key];

        public void Load()
        {
            Rows.Clear();

            Company company = _services.CompanyContext.Active;
            HasCompany = company != null;
            if (company == null)
            {
                HasEmployees = false;
                IsRestDay = false; RestDayText = string.Empty;
                Raise(nameof(HasRows)); Recompute();
                return;
            }

            // The company calendar + the day's holiday (advisory — a failure never blocks entry).
            CompanyWorkCalendar cal = null;
            Holiday holiday = null;
            try
            {
                cal = _services.WorkCalendar?.GetSettings(company.Id);
                holiday = _services.Holidays?.GetForYear(company.Id, Date.Year)
                    ?.FirstOrDefault(h => h.HolidayDate.Date == Date.Date);
            }
            catch { /* ignore */ }

            bool isHoliday = holiday != null;
            DayOfWeek dow = Date.DayOfWeek;

            // Today's recorded statuses (one query), keyed by employee.
            var byEmployee = _services.Attendance.GetCompanyDay(company.Id, Date)
                .GroupBy(r => r.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First().Status);

            List<Employee> employees = _services.Employees.GetByCompany(company.Id)
                .OrderBy(e => (e.LastNameFr + " " + e.FirstNameFr).Trim(), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            HasEmployees = employees.Count > 0;

            foreach (Employee e in employees)
            {
                // Expected today unless it is a public holiday, or a rest day under HIS effective pattern
                // (own weekly schedule when set, otherwise the company calendar). A guard whose own pattern
                // works Friday is expected on Friday even though it is the company's rest day.
                bool expected = !isHoliday && (cal == null || e.WorksOn(dow, cal));
                if (!expected) continue;

                AttendanceStatus st = byEmployee.TryGetValue(e.Id, out AttendanceStatus s) ? s : AttendanceStatus.Present;
                string name = (e.LastNameFr + " " + e.FirstNameFr).Trim();
                string dept = string.IsNullOrWhiteSpace(e.Department) ? "—" : e.Department;
                var row = new DailyRowViewModel(e.Id, name, dept, st, PrimaryStatuses, MoreStatuses);
                row.Picked += status => SetStatus(row, status);
                Rows.Add(row);
            }

            // Banner only when nobody is expected today: a holiday, or a company rest day with no one
            // scheduled. When at least one person is scheduled, the list is shown and there is no banner.
            if (Rows.Count == 0 && HasEmployees)
            {
                IsRestDay = true;
                if (isHoliday)
                {
                    bool rtl = _services.Localization != null && _services.Localization.IsRightToLeft;
                    string hn = rtl ? holiday.NameAr : null;
                    RestDayText = L("Att_Today_Holiday") + (string.IsNullOrWhiteSpace(hn) ? string.Empty : " — " + hn);
                }
                else
                {
                    RestDayText = L("Att_Today_RestDay");
                }
            }
            else
            {
                IsRestDay = false;
                RestDayText = string.Empty;
            }

            Raise(nameof(HasRows));
            Recompute();
        }

        private void SetStatus(DailyRowViewModel row, AttendanceStatus status)
        {
            Company company = _services.CompanyContext.Active;
            if (company == null) return;

            Result result = status == AttendanceStatus.Present
                ? _services.Attendance.ClearDay(row.EmployeeId, Date)   // Present is the default → remove the exception
                : _services.Attendance.SetDayStatus(row.EmployeeId, Date, status);

            if (result.IsFailure)
            {
                StatusMessage = result.Error;
                return;
            }

            row.SetStatus(status);
            row.MoreOpen = false;   // a pick closes the "more" popup
            Recompute();
            StatusMessage = row.Name + " · " + AttendanceAppearance.Label(status);
        }

        private void Recompute()
        {
            int present = Rows.Count(r => r.Status == AttendanceStatus.Present);
            int absent = Rows.Count(r => r.Status == AttendanceStatus.Absent);
            int leave = Rows.Count(r => r.Status == AttendanceStatus.Leave || r.Status == AttendanceStatus.JustifiedAbsence);
            int half = Rows.Count(r => r.Status == AttendanceStatus.HalfDay);

            SummaryLine = string.Format("{0} {1}   ·   {2} {3}   ·   {4} {5}   ·   {6} {7}",
                L("Att_Today_Present"), present,
                L("Att_Today_Absent"), absent,
                L("Att_Today_Leave"), leave,
                L("Att_Today_Half"), half);
        }
    }
}
