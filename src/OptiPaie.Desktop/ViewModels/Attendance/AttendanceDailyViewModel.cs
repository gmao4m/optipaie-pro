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
    /// <summary>
    /// One status chip on a daily row: a coloured, one-click button for a status. The active one
    /// (the employee's current status that day) is highlighted.
    /// </summary>
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

    /// <summary>One employee's row on the daily sheet. Everyone is Present by default; a single click
    /// on a chip sets a status and auto-saves.</summary>
    public sealed class DailyRowViewModel : ObservableObject
    {
        private AttendanceStatus _status;

        public DailyRowViewModel(long employeeId, string name, string department,
            AttendanceStatus status, IReadOnlyList<AttendanceStatus> statuses)
        {
            EmployeeId = employeeId;
            Name = name;
            Department = department;
            foreach (AttendanceStatus s in statuses)
            {
                AttendanceStatus captured = s;
                Chips.Add(new DailyChipViewModel(s, () => Picked?.Invoke(captured)));
            }
            SetStatus(status);
        }

        /// <summary>Raised when the user clicks a status chip on this row (the chosen status).</summary>
        public event Action<AttendanceStatus> Picked;

        public long EmployeeId { get; }
        public string Name { get; }
        public string Department { get; }
        public ObservableCollection<DailyChipViewModel> Chips { get; } = new ObservableCollection<DailyChipViewModel>();

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
            foreach (DailyChipViewModel c in Chips) c.IsActive = c.Status == status;
        }
    }

    /// <summary>
    /// TODAY — the attendance entry screen. Opens on today, one row per employee, everyone Present by
    /// default; the user touches only the exceptions, one click, and it auto-saves. A live line shows
    /// how many are present / absent / on leave / half day. A rest day or holiday is announced plainly.
    /// Reads and writes the SAME records the month matrix and payroll use — nothing re-entered.
    /// </summary>
    public sealed class AttendanceDailyViewModel : ObservableObject
    {
        // The statuses offered as one-click chips on a daily row (the common daily cases). Present is
        // the default (no record). The rarer corrections (worked-a-rest-day, holiday) live in the month.
        private static readonly AttendanceStatus[] DailyStatuses =
        {
            AttendanceStatus.Present,
            AttendanceStatus.Absent,
            AttendanceStatus.JustifiedAbsence,
            AttendanceStatus.Leave,
            AttendanceStatus.HalfDay,
            AttendanceStatus.Late,
            AttendanceStatus.Mission
        };

        private readonly AppServices _services;
        private DateTime _date = DateTime.Today;
        private bool _isRestDay;
        private string _restDayText = string.Empty;
        private bool _hasCompany = true;
        private string _statusMessage = string.Empty;
        private string _summaryLine = string.Empty;

        public AttendanceDailyViewModel(AppServices services)
        {
            _services = services;
            PrevDayCommand = new RelayCommand(() => Date = Date.AddDays(-1));
            NextDayCommand = new RelayCommand(() => Date = Date.AddDays(1));
            TodayCommand = new RelayCommand(() => Date = DateTime.Today);
        }

        public ObservableCollection<DailyRowViewModel> Rows { get; } = new ObservableCollection<DailyRowViewModel>();

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
        public bool HasEmployees => Rows.Count > 0;
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
            if (company == null) { IsRestDay = false; RestDayText = string.Empty; Recompute(); Raise(nameof(HasEmployees)); return; }

            // Rest day (work calendar) or public holiday → announce plainly.
            IsRestDay = false;
            RestDayText = string.Empty;
            try
            {
                CompanyWorkCalendar cal = _services.WorkCalendar?.GetSettings(company.Id);
                bool restByWeek = cal != null && !cal.IsWorkDay(Date.DayOfWeek);
                Holiday holiday = _services.Holidays?.GetForYear(company.Id, Date.Year)
                    ?.FirstOrDefault(h => h.HolidayDate.Date == Date.Date);
                if (holiday != null)
                {
                    IsRestDay = true;
                    // The holiday catalogue stores the Arabic name; show it only in Arabic to avoid mixing scripts.
                    bool rtl = _services.Localization != null && _services.Localization.IsRightToLeft;
                    string name = rtl ? holiday.NameAr : null;
                    RestDayText = L("Att_Today_Holiday") + (string.IsNullOrWhiteSpace(name) ? string.Empty : " — " + name);
                }
                else if (restByWeek)
                {
                    IsRestDay = true;
                    RestDayText = L("Att_Today_RestDay");
                }
            }
            catch { /* calendar/holidays are advisory; never block entry */ }

            // Each active employee, with the status recorded that day (default Present).
            var byEmployee = _services.Attendance.GetCompanyDay(company.Id, Date)
                .GroupBy(r => r.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First().Status);

            foreach (Employee e in _services.Employees.GetByCompany(company.Id)
                         .OrderBy(e => (e.LastNameFr + " " + e.FirstNameFr).Trim(), StringComparer.CurrentCultureIgnoreCase))
            {
                AttendanceStatus st = byEmployee.TryGetValue(e.Id, out AttendanceStatus s) ? s : AttendanceStatus.Present;
                string name = (e.LastNameFr + " " + e.FirstNameFr).Trim();
                string dept = string.IsNullOrWhiteSpace(e.Department) ? "—" : e.Department;
                var row = new DailyRowViewModel(e.Id, name, dept, st, DailyStatuses);
                row.Picked += status => SetStatus(row, status);
                Rows.Add(row);
            }

            Raise(nameof(HasEmployees));
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
