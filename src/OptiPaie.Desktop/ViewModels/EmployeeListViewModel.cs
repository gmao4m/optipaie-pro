using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// A read-only nominative list of employees — the drill-down opened when a dashboard workforce
    /// figure is clicked. Given the exact ids that compose the figure, it shows who they are. No writes.
    /// </summary>
    public sealed class EmployeeListViewModel : ObservableObject
    {
        public EmployeeListViewModel(AppServices services, string title, long companyId, IReadOnlyList<long> employeeIds)
        {
            Title = title;
            var wanted = new HashSet<long>(employeeIds ?? new List<long>());

            var byId = services.Employees.GetByCompany(companyId, true).Where(e => wanted.Contains(e.Id));
            bool rtl = services.Localization.IsRightToLeft;
            DateTime today = DateTime.Today;
            foreach (Employee e in byId.OrderBy(e => (e.LastNameFr + " " + e.FirstNameFr).Trim()))
                Rows.Add(new EmployeeListRow(e, services, rtl, today));

            CountText = Rows.Count.ToString(CultureInfo.InvariantCulture);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        public string Title { get; }
        public string CountText { get; }
        public ObservableCollection<EmployeeListRow> Rows { get; } = new ObservableCollection<EmployeeListRow>();
        public bool IsEmpty => Rows.Count == 0;

        public Action RequestClose { get; set; }
        public ICommand CloseCommand { get; }
    }

    /// <summary>One employee row of the drill-down list.</summary>
    public sealed class EmployeeListRow
    {
        public EmployeeListRow(Employee e, AppServices services, bool rtl, DateTime today)
        {
            Matricule = e.Id.ToString("0000", CultureInfo.InvariantCulture);
            string fr = (e.LastNameFr + " " + e.FirstNameFr).Trim();
            string ar = ((e.LastNameAr ?? string.Empty) + " " + (e.FirstNameAr ?? string.Empty)).Trim();
            Name = rtl && ar.Length > 0 ? ar : (fr.Length > 0 ? fr : ar);
            Poste = string.IsNullOrWhiteSpace(e.Poste) ? "—" : e.Poste;
            Department = string.IsNullOrWhiteSpace(e.Department) ? "—" : e.Department;
            Contract = services.Localization.GetString("Enum_ContractType_" + e.ContractType);
            HireDate = e.HireDate == default(DateTime) ? "—" : e.HireDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"));

            int years = 0;
            if (e.HireDate != default(DateTime) && e.HireDate.Date <= today)
            {
                years = today.Year - e.HireDate.Year;
                if (e.HireDate.Date > today.AddYears(-years)) years--;
            }
            Seniority = string.Format(services.Localization.GetString("Workforce_AgeYears"), years); // « N ans »
        }

        public string Matricule { get; }
        public string Name { get; }
        public string Poste { get; }
        public string Department { get; }
        public string Contract { get; }
        public string HireDate { get; }
        public string Seniority { get; }
    }
}
