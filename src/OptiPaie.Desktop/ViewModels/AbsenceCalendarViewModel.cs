using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    public sealed class AbsenceRow
    {
        public AbsenceRow(LeaveRequest r, string employeeName)
        {
            EmployeeName = employeeName;
            TypeLabel = LeaveLabels.Type(r.Type);
            Period = r.StartDate.ToString("dd/MM/yyyy") + " → " + r.EndDate.ToString("dd/MM/yyyy");
            DaysText = r.Days.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public string EmployeeName { get; }
        public string TypeLabel { get; }
        public string Period { get; }
        public string DaysText { get; }
    }

    /// <summary>Who is absent and when — approved leaves of a company for a year, to avoid emptying a team.</summary>
    public sealed class AbsenceCalendarViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _companyId;
        private int _year = DateTime.Today.Year;
        private string _statusMessage = string.Empty;

        public AbsenceCalendarViewModel(AppServices services, Company company, int year)
        {
            _services = services;
            _companyId = company.Id;
            CompanyName = company.NameFr;
            _year = year;

            for (int y = DateTime.Today.Year - 3; y <= DateTime.Today.Year + 1; y++) Years.Add(y);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            Load();
        }

        public Action RequestClose { get; set; }
        public string CompanyName { get; }
        public ObservableCollection<AbsenceRow> Absences { get; } = new ObservableCollection<AbsenceRow>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public int Year { get => _year; set { if (Set(ref _year, value)) Load(); } }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public ICommand CloseCommand { get; }

        private void Load()
        {
            Absences.Clear();

            var names = new Dictionary<long, string>();
            foreach (Employee e in _services.Employees.GetByCompany(_companyId))
                names[e.Id] = (e.LastNameFr + " " + e.FirstNameFr).Trim();

            var approved = _services.Leave.GetByCompanyYear(_companyId, _year)
                .Where(r => r.Status == LeaveStatus.Approved)
                .OrderBy(r => r.StartDate);

            foreach (LeaveRequest r in approved)
            {
                names.TryGetValue(r.EmployeeId, out string name);
                Absences.Add(new AbsenceRow(r, name ?? "—"));
            }

            StatusMessage = Absences.Count + " absence(s) approuvée(s) en " + _year;
        }
    }
}
