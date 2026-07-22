using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The task-first home. Shows the month's key figures and the primary actions a
    /// company owner needs, with no data grid in sight.
    /// </summary>
    public sealed class HomeViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;
        private readonly Action<string> _navigate;

        private int _companies;
        private int _employees;
        private string _monthLabel = string.Empty;

        public HomeViewModel(AppServices services, Action<string> navigate)
        {
            _services = services;
            _navigate = navigate;

            NewPayrollCommand = new RelayCommand(() => _navigate("payroll"));
            NewEmployeeCommand = new RelayCommand(() => _navigate("employees"));
            ArchiveCommand = new RelayCommand(() => _navigate("archive"));

            Recent = new ObservableCollection<ActivityItem>();
        }

        public string Greeting => _services.Localization.GetString("Home_Greeting");
        public string Subtitle => _services.Localization.GetString("Home_Subtitle");

        public string MonthLabel
        {
            get => _monthLabel;
            private set => Set(ref _monthLabel, value);
        }

        public int Companies
        {
            get => _companies;
            private set => Set(ref _companies, value);
        }

        public int Employees
        {
            get => _employees;
            private set => Set(ref _employees, value);
        }

        public ObservableCollection<ActivityItem> Recent { get; }

        public bool HasRecent => Recent.Count > 0;

        public ICommand NewPayrollCommand { get; }
        public ICommand NewEmployeeCommand { get; }
        public ICommand ArchiveCommand { get; }

        public void OnActivated()
        {
            Raise(nameof(Greeting));
            Raise(nameof(Subtitle));

            DateTime now = DateTime.Now;
            CultureInfo culture = _services.Localization.CurrentCulture;
            string month = culture.DateTimeFormat.GetMonthName(now.Month);
            if (month.Length > 0)
            {
                month = char.ToUpper(month[0], culture) + month.Substring(1);
            }

            MonthLabel = month + " " + now.Year;

            System.Collections.Generic.IReadOnlyList<Company> companies = _services.Companies.GetAll();
            Companies = companies.Count;

            int employees = 0;
            foreach (Company company in companies)
            {
                employees += _services.Employees.GetByCompany(company.Id).Count;
            }

            Employees = employees;
            Raise(nameof(HasRecent));
        }

        /// <summary>A single recent-activity row.</summary>
        public sealed class ActivityItem
        {
            public string IconKey { get; set; }
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public string When { get; set; }
        }
    }
}
