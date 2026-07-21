using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Creates or edits a leave request. The number of days is computed live by the
    /// service (rest days excluded), so what the user sees is exactly what will be
    /// deducted from the balance and written into attendance.
    /// </summary>
    public sealed class LeaveEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly LeaveRequest _request;

        private Employee _selectedEmployee;
        private LeaveTypeOption _selectedType;
        private DateTime _startDate;
        private DateTime _endDate;
        private string _reason;
        private string _daysText = "0";

        public LeaveEditViewModel(AppServices services, IReadOnlyList<Employee> employees, LeaveRequest existing)
        {
            _services = services;
            _request = existing ?? new LeaveRequest();

            foreach (Employee employee in employees) Employees.Add(employee);
            foreach (LeaveType type in Enum.GetValues(typeof(LeaveType))) Types.Add(new LeaveTypeOption(type));

            if (existing != null)
            {
                _startDate = existing.StartDate;
                _endDate = existing.EndDate;
                _reason = existing.Reason;
                _selectedType = Types.FirstOrDefaultByValue(existing.Type);
                _selectedEmployee = Employees.FirstOrDefaultById(existing.EmployeeId);
                Title = "Modifier la demande";
            }
            else
            {
                _startDate = DateTime.Today;
                _endDate = DateTime.Today;
                _selectedType = Types.FirstOrDefaultByValue(LeaveType.Annual);
                _selectedEmployee = Employees.Count > 0 ? Employees[0] : null;
                Title = "Nouvelle demande de congé";
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            RecomputeDays();
        }

        /// <summary>Set by the host window: true = saved, false = cancelled.</summary>
        public Action<bool> RequestClose { get; set; }

        public string Title { get; }

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<LeaveTypeOption> Types { get; } = new ObservableCollection<LeaveTypeOption>();

        /// <summary>The employee cannot be changed once the request exists.</summary>
        public bool CanChooseEmployee => _request.Id == 0;

        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set => Set(ref _selectedEmployee, value);
        }

        public LeaveTypeOption SelectedType
        {
            get => _selectedType;
            set => Set(ref _selectedType, value);
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (!Set(ref _startDate, value)) return;
                if (_endDate < _startDate) EndDate = _startDate;
                RecomputeDays();
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set { if (Set(ref _endDate, value)) RecomputeDays(); }
        }

        public string Reason { get => _reason; set => Set(ref _reason, value); }

        /// <summary>Days that will actually be consumed (Friday/Saturday excluded).</summary>
        public string DaysText { get => _daysText; private set => Set(ref _daysText, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void RecomputeDays()
        {
            decimal days = _endDate < _startDate ? 0m : _services.Leave.CountDays(_startDate, _endDate);
            DaysText = days.ToString("0.##", CultureInfo.InvariantCulture) + " jour(s) décompté(s)";
        }

        private void Save()
        {
            if (_selectedEmployee == null)
            {
                Dialogs.Error("Sélectionnez un employé.");
                return;
            }

            if (_selectedType == null)
            {
                Dialogs.Error("Sélectionnez un type de congé.");
                return;
            }

            _request.EmployeeId = _selectedEmployee.Id;
            _request.Type = _selectedType.Value;
            _request.StartDate = _startDate;
            _request.EndDate = _endDate;
            _request.Reason = _reason;

            Result<long> result = _services.Leave.Save(_request);
            if (result.IsFailure)
            {
                Dialogs.Error(result.Error);
                return;
            }

            RequestClose?.Invoke(true);
        }
    }

    /// <summary>Small lookup helpers keeping the view model readable.</summary>
    internal static class LeaveEditExtensions
    {
        public static LeaveTypeOption FirstOrDefaultByValue(this ObservableCollection<LeaveTypeOption> options, LeaveType value)
        {
            foreach (LeaveTypeOption option in options)
            {
                if (option.Value == value) return option;
            }

            return null;
        }

        public static Employee FirstOrDefaultById(this ObservableCollection<Employee> employees, long id)
        {
            foreach (Employee employee in employees)
            {
                if (employee.Id == id) return employee;
            }

            return null;
        }
    }
}
