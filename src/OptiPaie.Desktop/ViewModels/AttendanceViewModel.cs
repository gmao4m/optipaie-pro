using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>A selectable attendance status with its French label.</summary>
    public sealed class AttendanceStatusOption
    {
        public AttendanceStatusOption(AttendanceStatus value, string label) { Value = value; Label = label; }
        public AttendanceStatus Value { get; }
        public string Label { get; }
    }

    /// <summary>One employee's row in the daily attendance sheet.</summary>
    public sealed class AttendanceRowViewModel : ObservableObject
    {
        private AttendanceStatus _status;
        private string _checkIn, _checkOut, _notes;

        public AttendanceRowViewModel(Employee employee, AttendanceRecord record, string defaultStart)
        {
            EmployeeId = employee.Id;
            EmployeeName = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
            Poste = employee.Poste ?? string.Empty;

            if (record != null)
            {
                _status = record.Status;
                _checkIn = record.CheckIn;
                _checkOut = record.CheckOut;
                _notes = record.Notes;
                WorkedHoursText = record.WorkedHours.ToString("0.##", CultureInfo.InvariantCulture);
                LateText = record.LateMinutes > 0 ? record.LateMinutes + " min" : "—";
                OvertimeText = record.OvertimeHours > 0m ? record.OvertimeHours.ToString("0.##", CultureInfo.InvariantCulture) : "—";
            }
            else
            {
                _status = AttendanceStatus.Present;
                _checkIn = defaultStart;
                WorkedHoursText = "—";
                LateText = "—";
                OvertimeText = "—";
            }
        }

        public long EmployeeId { get; }
        public string EmployeeName { get; }
        public string Poste { get; }

        public AttendanceStatus Status { get => _status; set => Set(ref _status, value); }
        public string CheckIn { get => _checkIn; set => Set(ref _checkIn, value); }
        public string CheckOut { get => _checkOut; set => Set(ref _checkOut, value); }
        public string Notes { get => _notes; set => Set(ref _notes, value); }

        /// <summary>Derived values, recomputed by the service and refreshed on save.</summary>
        public string WorkedHoursText { get; }
        public string LateText { get; }
        public string OvertimeText { get; }
    }

    /// <summary>
    /// Attendance — daily sheet for a company. Employees come from the SHARED
    /// employee repository (never duplicated); all hour/lateness/overtime maths lives
    /// in <see cref="Core.Interfaces.Services.IAttendanceService"/>, so the grid always
    /// shows exactly what payroll will consume.
    /// </summary>
    public sealed class AttendanceViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;

        private Company _selectedCompany;
        private DateTime _selectedDate = DateTime.Today;
        private string _presentText = "0", _absentText = "0", _lateText = "0", _overtimeText = "0";
        private string _statusMessage = string.Empty;
        private bool _loading;

        public AttendanceViewModel(AppServices services)
        {
            _services = services;
            SaveCommand = new RelayCommand(Save);
            MarkAllPresentCommand = new RelayCommand(MarkAllPresent);
            MonthSummaryCommand = new RelayCommand(OpenMonthSummary);
            RefreshCommand = new RelayCommand(LoadDay);
            SettingsCommand = new RelayCommand(OpenSettings);
            TodayCommand = new RelayCommand(() => SelectedDate = DateTime.Today);
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();
        public ObservableCollection<AttendanceRowViewModel> Rows { get; } = new ObservableCollection<AttendanceRowViewModel>();

        public List<AttendanceStatusOption> StatusOptions { get; } = new List<AttendanceStatusOption>
        {
            new AttendanceStatusOption(AttendanceStatus.Present, "Présent"),
            new AttendanceStatusOption(AttendanceStatus.Absent, "Absent"),
            new AttendanceStatusOption(AttendanceStatus.Late, "Retard"),
            new AttendanceStatusOption(AttendanceStatus.Leave, "Congé"),
            new AttendanceStatusOption(AttendanceStatus.Holiday, "Jour férié"),
            new AttendanceStatusOption(AttendanceStatus.Rest, "Repos")
        };

        public Company SelectedCompany
        {
            get => _selectedCompany;
            set { if (Set(ref _selectedCompany, value)) LoadDay(); }
        }

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set { if (Set(ref _selectedDate, value)) { Raise(nameof(DateLabel)); LoadDay(); } }
        }

        public string DateLabel => _selectedDate.ToString("dddd d MMMM yyyy", new CultureInfo("fr-FR"));

        public string PresentText { get => _presentText; private set => Set(ref _presentText, value); }
        public string AbsentText { get => _absentText; private set => Set(ref _absentText, value); }
        public string LateText { get => _lateText; private set => Set(ref _lateText, value); }
        public string OvertimeText { get => _overtimeText; private set => Set(ref _overtimeText, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand SaveCommand { get; }
        public ICommand MarkAllPresentCommand { get; }
        public ICommand MonthSummaryCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand TodayCommand { get; }
        public ICommand SettingsCommand { get; }

        public void OnActivated()
        {
            IReadOnlyList<Company> companies = _services.Companies.GetAll();
            Companies.Clear();
            foreach (Company c in companies) Companies.Add(c);

            if (_selectedCompany == null && Companies.Count > 0)
            {
                SelectedCompany = Companies[0]; // triggers LoadDay
            }
            else
            {
                LoadDay();
            }
        }

        private void LoadDay()
        {
            if (_loading) return;
            _loading = true;
            try
            {
                Rows.Clear();
                if (_selectedCompany == null)
                {
                    UpdateKpis();
                    return;
                }

                string defaultStart = _services.Attendance.GetSettings().StandardStart;
                IReadOnlyList<Employee> employees = _services.Employees.GetByCompany(_selectedCompany.Id, false);
                var existing = _services.Attendance.GetCompanyDay(_selectedCompany.Id, _selectedDate)
                    .ToDictionary(r => r.EmployeeId, r => r);

                foreach (Employee e in employees)
                {
                    existing.TryGetValue(e.Id, out AttendanceRecord record);
                    Rows.Add(new AttendanceRowViewModel(e, record, defaultStart));
                }

                UpdateKpis();
                StatusMessage = Rows.Count == 0
                    ? "Aucun employé actif dans cette entreprise."
                    : Rows.Count + " employé(s) · " + existing.Count + " pointage(s) enregistré(s)";
            }
            finally
            {
                _loading = false;
            }
        }

        private void UpdateKpis()
        {
            if (_selectedCompany == null)
            {
                PresentText = AbsentText = LateText = "0";
                OvertimeText = "0";
                return;
            }

            var records = _services.Attendance.GetCompanyDay(_selectedCompany.Id, _selectedDate);
            PresentText = records.Count(r => r.Status == AttendanceStatus.Present || r.Status == AttendanceStatus.Late).ToString();
            AbsentText = records.Count(r => r.Status == AttendanceStatus.Absent).ToString();
            LateText = records.Count(r => r.LateMinutes > 0).ToString();
            OvertimeText = records.Sum(r => r.OvertimeHours).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void MarkAllPresent()
        {
            string start = _services.Attendance.GetSettings().StandardStart;
            foreach (AttendanceRowViewModel row in Rows)
            {
                row.Status = AttendanceStatus.Present;
                if (string.IsNullOrWhiteSpace(row.CheckIn)) row.CheckIn = start;
            }

            StatusMessage = "Tous marqués présents — enregistrez pour valider.";
        }

        private void Save()
        {
            if (_selectedCompany == null || Rows.Count == 0) return;

            var records = Rows.Select(r => new AttendanceRecord
            {
                EmployeeId = r.EmployeeId,
                WorkDate = _selectedDate.Date,
                Status = r.Status,
                CheckIn = r.CheckIn,
                CheckOut = r.CheckOut,
                Notes = r.Notes
            }).ToList();

            Result result = _services.Attendance.SaveMany(records);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            LoadDay();
            StatusMessage = "Pointage enregistré pour le " + _selectedDate.ToString("dd/MM/yyyy") + ".";
        }

        private void OpenSettings()
        {
            var vm = new AttendanceSettingsViewModel(_services.Attendance);
            var window = new AttendanceSettingsWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;

            if (window.ShowDialog() == true)
            {
                // Rebuild the day so the grid reflects the new rules immediately.
                LoadDay();
                StatusMessage = "Paramètres enregistrés.";
            }
        }

        private void OpenMonthSummary()
        {
            if (_selectedCompany == null)
            {
                Dialogs.Info("Sélectionnez d'abord une entreprise.");
                return;
            }

            var vm = new AttendanceMonthViewModel(_services, _selectedCompany, _selectedDate.Year, _selectedDate.Month);
            var window = new AttendanceMonthWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = () => window.Close();
            window.ShowDialog();
        }
    }
}
