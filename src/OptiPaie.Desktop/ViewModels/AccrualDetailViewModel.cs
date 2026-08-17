using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    public sealed class AccrualMonthRow
    {
        public AccrualMonthRow(AccrualMonth m)
        {
            MonthName = CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetMonthName(m.Month);
            Present = m.Present ? "✔" : "—";
            UnpaidDays = m.UnpaidDays.ToString("0.##", CultureInfo.InvariantCulture);
            Accrued = m.Accrued.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public string MonthName { get; }
        public string Present { get; }
        public string UnpaidDays { get; }
        public string Accrued { get; }
    }

    /// <summary>Month-by-month annual-leave accrual detail for one employee.</summary>
    public sealed class AccrualDetailViewModel : ObservableObject
    {
        public AccrualDetailViewModel(AppServices services, long employeeId, string employeeName, int year)
        {
            EmployeeName = employeeName;
            Year = year;

            var detail = services.Leave.GetAccrualDetail(employeeId, year);
            foreach (AccrualMonth m in detail) Months.Add(new AccrualMonthRow(m));
            TotalText = detail.Sum(m => m.Accrued).ToString("0.##", CultureInfo.InvariantCulture) + " jour(s) acquis";

            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        public Action RequestClose { get; set; }
        public string EmployeeName { get; }
        public int Year { get; }
        public ObservableCollection<AccrualMonthRow> Months { get; } = new ObservableCollection<AccrualMonthRow>();
        public string TotalText { get; }
        public ICommand CloseCommand { get; }
    }
}
