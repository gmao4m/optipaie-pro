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

namespace OptiPaie.Desktop.ViewModels.Attendance
{
    /// <summary>
    /// The Attendance Matrix — one professional, Excel-like screen to manage the whole
    /// company's month. Employees are rows; every day of the month is a colour-coded
    /// column. Pick a status "brush", click cells (or a day header, or bulk over the
    /// selected employees) and it auto-saves. KPIs update live; payroll consumes the
    /// same records. Built for hundreds of employees (the grid virtualises).
    /// </summary>
    public sealed class AttendanceMatrixViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private const string AllDepartments = "Tous les départements";

        private readonly AppServices _services;
        private readonly List<MatrixRowViewModel> _allRows = new List<MatrixRowViewModel>();

        private Company _selectedCompany;
        private string _selectedDepartment = AllDepartments;
        private int _selectedMonth = DateTime.Today.Month;
        private int _selectedYear = DateTime.Today.Year;
        private string _search = string.Empty;
        private StatusBrushViewModel _selectedBrush;
        private AttendanceStatus? _statusFilter;
        private int _dayCount = DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month);
        private string _statusMessage = string.Empty;
        private bool _loading;

        // KPI strings (formatted for direct binding).
        private string _kpiAttendance = "0 %", _kpiAbsence = "0 %", _kpiLate = "0 %";
        private string _kpiWorkingDays = "0", _kpiPresentToday = "0", _kpiOnLeave = "0", _kpiOnMission = "0", _kpiEmployees = "0";

        public AttendanceMatrixViewModel(AppServices services)
        {
            _services = services;

            for (int m = 1; m <= 12; m++) Months.Add(new MonthOption(m, Fr.DateTimeFormat.GetMonthName(m)));
            for (int y = DateTime.Today.Year - 5; y <= DateTime.Today.Year + 1; y++) Years.Add(y);

            foreach (AttendanceStatus s in AttendanceAppearance.Palette)
            {
                Palette.Add(new StatusBrushViewModel(s));
            }
            _selectedBrush = Palette.FirstOrDefault(b => b.Status == AttendanceStatus.Present);
            if (_selectedBrush != null) _selectedBrush.IsSelected = true;

            StatusFilters.Add(new StatusFilterOption("Tous les statuts", null));
            foreach (AttendanceStatus s in AttendanceAppearance.Palette)
            {
                StatusFilters.Add(new StatusFilterOption(AttendanceAppearance.Label(s), s));
            }
            SelectedStatusFilter = StatusFilters[0];

            SelectBrushCommand = new RelayCommand(p => SelectBrush(p as StatusBrushViewModel));
            PaintCellCommand = new RelayCommand(p => PaintCell(p as MatrixCellViewModel));
            PaintDayCommand = new RelayCommand(PaintDay);
            ApplyMonthToSelectionCommand = new RelayCommand(ApplyMonthToSelection);
            SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
            ClearSelectionCommand = new RelayCommand(() => SetAllSelected(false));
            EmployeeDetailCommand = new RelayCommand(p => OpenEmployeeDetail(p as MatrixRowViewModel));
            MonthSummaryCommand = new RelayCommand(OpenMonthSummary);
            SettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new RelayCommand(Load);
            TodayCommand = new RelayCommand(GoToday);
        }

        /// <summary>Raised after the rows/day-count are rebuilt so the view regenerates columns.</summary>
        public event EventHandler LayoutChanged;

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<string> Departments { get; } = new ObservableCollection<string>();
        public ObservableCollection<MonthOption> Months { get; } = new ObservableCollection<MonthOption>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<StatusBrushViewModel> Palette { get; } = new ObservableCollection<StatusBrushViewModel>();
        public ObservableCollection<StatusFilterOption> StatusFilters { get; } = new ObservableCollection<StatusFilterOption>();

        /// <summary>The rows currently shown in the grid (after search / status filter).</summary>
        public ObservableCollection<MatrixRowViewModel> Rows { get; } = new ObservableCollection<MatrixRowViewModel>();

        public int DayCount { get => _dayCount; private set => Set(ref _dayCount, value); }

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) { RebuildDepartments(); Load(); } }
        }

        public string SelectedDepartment
        {
            get => _selectedDepartment;
            set { if (Set(ref _selectedDepartment, value)) Load(); }
        }

        public int SelectedMonth
        {
            get => _selectedMonth;
            set { if (Set(ref _selectedMonth, value)) { Raise(nameof(PeriodLabel)); Load(); } }
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set { if (Set(ref _selectedYear, value)) { Raise(nameof(PeriodLabel)); Load(); } }
        }

        public string PeriodLabel => Fr.DateTimeFormat.GetMonthName(_selectedMonth) + " " + _selectedYear;

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) ApplyFilter(); }
        }

        public StatusBrushViewModel SelectedBrush
        {
            get => _selectedBrush;
            private set => Set(ref _selectedBrush, value);
        }

        public StatusFilterOption SelectedStatusFilter
        {
            get => _statusFilter.HasValue
                ? StatusFilters.FirstOrDefault(o => o.Status == _statusFilter)
                : StatusFilters.FirstOrDefault(o => o.Status == null);
            set
            {
                _statusFilter = value?.Status;
                Raise(nameof(SelectedStatusFilter));
                ApplyFilter();
            }
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public string KpiAttendance { get => _kpiAttendance; private set => Set(ref _kpiAttendance, value); }
        public string KpiAbsence { get => _kpiAbsence; private set => Set(ref _kpiAbsence, value); }
        public string KpiLate { get => _kpiLate; private set => Set(ref _kpiLate, value); }
        public string KpiWorkingDays { get => _kpiWorkingDays; private set => Set(ref _kpiWorkingDays, value); }
        public string KpiPresentToday { get => _kpiPresentToday; private set => Set(ref _kpiPresentToday, value); }
        public string KpiOnLeave { get => _kpiOnLeave; private set => Set(ref _kpiOnLeave, value); }
        public string KpiOnMission { get => _kpiOnMission; private set => Set(ref _kpiOnMission, value); }
        public string KpiEmployees { get => _kpiEmployees; private set => Set(ref _kpiEmployees, value); }

        public ICommand SelectBrushCommand { get; }
        public ICommand PaintCellCommand { get; }
        public ICommand PaintDayCommand { get; }
        public ICommand ApplyMonthToSelectionCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand EmployeeDetailCommand { get; }
        public ICommand MonthSummaryCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand TodayCommand { get; }

        public void OnActivated()
        {
            Companies.Clear();
            foreach (Company c in _services.Companies.GetAll()) Companies.Add(c);

            if (_selectedCompany == null && Companies.Count > 0)
            {
                SelectedCompany = Companies[0]; // triggers Load
            }
            else
            {
                Load();
            }
        }

        // ---------------------------------------------------------------- build

        private void RebuildDepartments()
        {
            Departments.Clear();
            Departments.Add(AllDepartments);
            if (_selectedCompany == null) { _selectedDepartment = AllDepartments; return; }

            foreach (string dept in _services.Employees.GetByCompany(_selectedCompany.Id, false)
                .Select(e => e.Department)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                Departments.Add(dept);
            }

            _selectedDepartment = AllDepartments;
            Raise(nameof(SelectedDepartment));
        }

        private void Load()
        {
            if (_loading) return;
            _loading = true;
            try
            {
                _allRows.Clear();
                DayCount = DateTime.DaysInMonth(_selectedYear, _selectedMonth);

                if (_selectedCompany == null)
                {
                    Rows.Clear();
                    RecomputeKpis();
                    LayoutChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
                var records = _services.Attendance.GetCompanyMonth(_selectedCompany.Id, _selectedYear, _selectedMonth);
                var byKey = records.ToDictionary(r => (r.EmployeeId, r.WorkDate.Day), r => r.Status);
                DateTime today = DateTime.Today;

                foreach (Employee employee in employees)
                {
                    if (!MatchesDepartment(employee)) continue;

                    var cells = new List<MatrixCellViewModel>(DayCount);
                    for (int day = 1; day <= DayCount; day++)
                    {
                        var date = new DateTime(_selectedYear, _selectedMonth, day);
                        bool weekend = date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday;
                        bool future = date > today;
                        AttendanceStatus? status = byKey.TryGetValue((employee.Id, day), out AttendanceStatus s) ? s : (AttendanceStatus?)null;
                        cells.Add(new MatrixCellViewModel(employee.Id, date, weekend, future, status));
                    }

                    _allRows.Add(new MatrixRowViewModel(employee, cells));
                }

                ApplyFilter();
                RecomputeKpis();
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _loading = false;
            }
        }

        private bool MatchesDepartment(Employee e)
        {
            if (string.Equals(_selectedDepartment, AllDepartments, StringComparison.Ordinal)) return true;
            return string.Equals(e.Department, _selectedDepartment, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyFilter()
        {
            string term = (_search ?? string.Empty).Trim();
            Rows.Clear();

            foreach (MatrixRowViewModel row in _allRows)
            {
                if (term.Length > 0 &&
                    row.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0 &&
                    row.Number.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (_statusFilter.HasValue && !row.Cells.Any(c => c.Status == _statusFilter))
                {
                    continue;
                }

                Rows.Add(row);
            }

            StatusMessage = Rows.Count + " / " + _allRows.Count + " employé(s) affiché(s) · " + PeriodLabel;
        }

        // ---------------------------------------------------------------- painting (auto-save)

        private void SelectBrush(StatusBrushViewModel brush)
        {
            if (brush == null) return;
            foreach (StatusBrushViewModel b in Palette) b.IsSelected = b == brush;
            SelectedBrush = brush;
        }

        private void PaintCell(MatrixCellViewModel cell)
        {
            if (cell == null) return;
            if (SelectedBrush == null)
            {
                StatusMessage = "Choisissez d'abord un statut dans la palette.";
                return;
            }

            if (cell.IsFuture)
            {
                StatusMessage = "Impossible de pointer une date future.";
                return;
            }

            if (cell.Paint(SelectedBrush.Status))
            {
                Result result = _services.Attendance.SetDayStatus(cell.EmployeeId, cell.Date, SelectedBrush.Status);
                if (result.IsFailure)
                {
                    Dialogs.Error(result.Error);
                    return;
                }

                RecomputeKpis();
            }
        }

        private void PaintDay(object parameter)
        {
            if (SelectedBrush == null || !TryDay(parameter, out int day)) return;

            var entries = new List<AttendanceDayStatus>();
            foreach (MatrixRowViewModel row in Rows)
            {
                MatrixCellViewModel cell = row.Cells[day - 1];
                if (cell.Paint(SelectedBrush.Status))
                {
                    entries.Add(new AttendanceDayStatus(cell.EmployeeId, cell.Date, SelectedBrush.Status));
                }
            }

            Persist(entries, "Jour " + day + " : " + AttendanceAppearance.Label(SelectedBrush.Status) + " (" + entries.Count + ")");
        }

        private void ApplyMonthToSelection()
        {
            if (SelectedBrush == null) return;
            List<MatrixRowViewModel> selected = Rows.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusMessage = "Cochez d'abord des employés pour l'action groupée.";
                return;
            }

            var entries = new List<AttendanceDayStatus>();
            foreach (MatrixRowViewModel row in selected)
            {
                foreach (MatrixCellViewModel cell in row.Cells)
                {
                    if (cell.IsFuture || cell.IsWeekend) continue; // fill working days only
                    if (cell.Paint(SelectedBrush.Status))
                    {
                        entries.Add(new AttendanceDayStatus(cell.EmployeeId, cell.Date, SelectedBrush.Status));
                    }
                }
            }

            Persist(entries, selected.Count + " employé(s) · " + AttendanceAppearance.Label(SelectedBrush.Status) +
                             " appliqué au mois (" + entries.Count + " jours)");
        }

        private void Persist(List<AttendanceDayStatus> entries, string message)
        {
            if (entries.Count == 0)
            {
                StatusMessage = "Aucune modification.";
                return;
            }

            Result result = _services.Attendance.SetDayStatusBulk(entries);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RecomputeKpis();
            StatusMessage = message;
        }

        private void SetAllSelected(bool selected)
        {
            foreach (MatrixRowViewModel row in Rows) row.IsSelected = selected;
        }

        // ---------------------------------------------------------------- KPIs

        private void RecomputeKpis()
        {
            int employees = _allRows.Count;
            int workingDays = 0;
            for (int day = 1; day <= DayCount; day++)
            {
                var date = new DateTime(_selectedYear, _selectedMonth, day);
                if (date.DayOfWeek != DayOfWeek.Friday && date.DayOfWeek != DayOfWeek.Saturday) workingDays++;
            }

            int present = 0, absent = 0, late = 0, leave = 0, mission = 0;
            int today = (_selectedYear == DateTime.Today.Year && _selectedMonth == DateTime.Today.Month) ? DateTime.Today.Day : 0;
            int presentToday = 0, onLeaveToday = 0, onMissionToday = 0;

            foreach (MatrixRowViewModel row in _allRows)
            {
                foreach (MatrixCellViewModel cell in row.Cells)
                {
                    switch (cell.Status)
                    {
                        case AttendanceStatus.Present: present++; break;
                        case AttendanceStatus.Late: late++; present++; break;
                        case AttendanceStatus.Mission: mission++; present++; break;
                        case AttendanceStatus.Absent: absent++; break;
                        case AttendanceStatus.Leave: leave++; break;
                    }

                    if (today > 0 && cell.Day == today)
                    {
                        if (cell.Status == AttendanceStatus.Present || cell.Status == AttendanceStatus.Late) presentToday++;
                        else if (cell.Status == AttendanceStatus.Leave) onLeaveToday++;
                        else if (cell.Status == AttendanceStatus.Mission) onMissionToday++;
                    }
                }
            }

            int possible = workingDays * employees;
            KpiEmployees = employees.ToString();
            KpiWorkingDays = workingDays.ToString();
            KpiAttendance = Percent(present, possible);
            KpiAbsence = Percent(absent, possible);
            KpiLate = Percent(late, possible);
            KpiPresentToday = presentToday.ToString();
            KpiOnLeave = onLeaveToday.ToString();
            KpiOnMission = onMissionToday.ToString();
        }

        private static string Percent(int part, int whole)
        {
            if (whole <= 0) return "0 %";
            return Math.Round(part * 100m / whole, 1).ToString("0.#", Fr) + " %";
        }

        // ---------------------------------------------------------------- dialogs

        private void OpenEmployeeDetail(MatrixRowViewModel row)
        {
            if (row == null) return;
            var vm = new AttendanceEmployeeDetailViewModel(_services, row.EmployeeId, row.Name, _selectedYear, _selectedMonth);
            var window = new AttendanceEmployeeDetailWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();

            // The detail view can change data — reflect it.
            Load();
        }

        private void OpenMonthSummary()
        {
            if (_selectedCompany == null) { Dialogs.Info("Sélectionnez une entreprise."); return; }
            var vm = new AttendanceMonthViewModel(_services, _selectedCompany, _selectedYear, _selectedMonth);
            var window = new AttendanceMonthWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
        }

        private void OpenSettings()
        {
            var vm = new AttendanceSettingsViewModel(_services.Attendance);
            var window = new AttendanceSettingsWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            if (window.ShowDialog() == true) Load();
        }

        private void GoToday()
        {
            SelectedYear = DateTime.Today.Year;
            SelectedMonth = DateTime.Today.Month;
        }

        private static bool TryDay(object parameter, out int day)
        {
            day = 0;
            if (parameter is int i) { day = i; return true; }
            return parameter != null && int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out day);
        }
    }

    /// <summary>A status quick-filter option (null = all).</summary>
    public sealed class StatusFilterOption
    {
        public StatusFilterOption(string label, AttendanceStatus? status) { Label = label; Status = status; }
        public string Label { get; }
        public AttendanceStatus? Status { get; }
    }
}
