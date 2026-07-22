using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels.Attendance
{
    /// <summary>One day box of the employee-detail calendar.</summary>
    public sealed class DetailDayViewModel
    {
        public DetailDayViewModel(int day, bool inMonth, AttendanceStatus? status, bool isWeekend)
        {
            Day = day;
            InMonth = inMonth;
            DayText = inMonth ? day.ToString(CultureInfo.InvariantCulture) : string.Empty;
            Background = inMonth ? AttendanceAppearance.Background(status, isWeekend, false) : Brushes.Transparent;
            Letter = inMonth ? AttendanceAppearance.Letter(status, isWeekend) : string.Empty;
        }

        public int Day { get; }
        public bool InMonth { get; }
        public string DayText { get; }
        public Brush Background { get; }
        public string Letter { get; }
    }

    /// <summary>
    /// Complete attendance history of one employee: a colour calendar for the chosen
    /// month, the month's statistics and the lists of late / absence days. Read-only —
    /// editing happens in the matrix.
    /// </summary>
    public sealed class AttendanceEmployeeDetailViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly long _employeeId;

        private int _year;
        private int _month;
        private string _statsText = string.Empty;
        private string _lateText = string.Empty;
        private string _absenceText = string.Empty;

        public AttendanceEmployeeDetailViewModel(AppServices services, long employeeId, string name, int year, int month)
        {
            _services = services;
            _employeeId = employeeId;
            EmployeeName = name;
            _year = year;
            _month = month;

            for (int m = 1; m <= 12; m++) Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));
            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);

            PrevCommand = new RelayCommand(() => Shift(-1));
            NextCommand = new RelayCommand(() => Shift(1));
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        public Action RequestClose { get; set; }

        public string EmployeeName { get; }
        public string[] WeekdayHeaders { get; } = { "Dim", "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam" };

        public ObservableCollection<DetailDayViewModel> Days { get; } = new ObservableCollection<DetailDayViewModel>();
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();

        public int SelectedMonth
        {
            get => _month;
            set { if (Set(ref _month, value)) Load(); }
        }

        public int SelectedYear
        {
            get => _year;
            set { if (Set(ref _year, value)) Load(); }
        }

        public string PeriodLabel => Fr.DateTimeFormat.GetMonthName(_month) + " " + _year;
        public string StatsText { get => _statsText; private set => Set(ref _statsText, value); }
        public string LateText { get => _lateText; private set => Set(ref _lateText, value); }
        public string AbsenceText { get => _absenceText; private set => Set(ref _absenceText, value); }

        public ICommand PrevCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand CloseCommand { get; }

        private void Shift(int months)
        {
            var d = new DateTime(_year, _month, 1).AddMonths(months);
            _year = d.Year; _month = d.Month;
            Raise(nameof(SelectedMonth));
            Raise(nameof(SelectedYear));
            Load();
        }

        private void Load()
        {
            Raise(nameof(PeriodLabel));
            Days.Clear();

            int dayCount = DateTime.DaysInMonth(_year, _month);
            AttendanceRecord[] month = _services.Attendance
                .GetEmployeeMonth(_employeeId, _year, _month).ToArray();
            var byDay = month.ToDictionary(r => r.WorkDate.Day, r => r);

            // Leading blanks (week starts Sunday = 0).
            int leading = (int)new DateTime(_year, _month, 1).DayOfWeek;
            for (int i = 0; i < leading; i++) Days.Add(new DetailDayViewModel(0, false, null, false));

            for (int day = 1; day <= dayCount; day++)
            {
                var date = new DateTime(_year, _month, day);
                bool weekend = date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday;
                AttendanceStatus? status = byDay.TryGetValue(day, out AttendanceRecord r) ? r.Status : (AttendanceStatus?)null;
                Days.Add(new DetailDayViewModel(day, true, status, weekend));
            }

            while (Days.Count % 7 != 0) Days.Add(new DetailDayViewModel(0, false, null, false));

            AttendanceSummary summary = _services.Attendance.GetMonthlySummary(_employeeId, _year, _month);
            StatsText =
                "Présents : " + summary.PresentDays + "   ·   Absents : " + summary.AbsentDays +
                "   ·   Retards : " + summary.LateCount + "   ·   Congés : " + summary.LeaveDays +
                "   ·   Fériés : " + summary.HolidayDays + Environment.NewLine +
                "Heures travaillées : " + summary.WorkedHours.ToString("0.##", Fr) +
                "   ·   Heures supp. : " + summary.OvertimeHours.ToString("0.##", Fr) +
                "   ·   Minutes de retard : " + summary.LateMinutes;

            List<int> lateDays = month.Where(x => x.Status == AttendanceStatus.Late || x.LateMinutes > 0)
                .Select(x => x.WorkDate.Day).OrderBy(d => d).ToList();
            List<int> absenceDays = month.Where(x => x.Status == AttendanceStatus.Absent)
                .Select(x => x.WorkDate.Day).OrderBy(d => d).ToList();

            LateText = lateDays.Count == 0 ? "Aucun retard ce mois." : "Retards : les " + string.Join(", ", lateDays);
            AbsenceText = absenceDays.Count == 0 ? "Aucune absence ce mois." : "Absences : les " + string.Join(", ", absenceDays);
        }
    }
}
