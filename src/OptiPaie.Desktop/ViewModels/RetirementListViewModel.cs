using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The nominative retirement list opened from the dashboard retirement indicator. For each
    /// concerned employee it shows the name, date of birth, current age and the date they reach legal
    /// retirement age — soonest first (those who have already passed the age come first). Read-only.
    /// </summary>
    public sealed class RetirementListViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public RetirementListViewModel(AppServices services, string title, long companyId, IReadOnlyList<RetirementCandidate> candidates)
        {
            Title = title;
            var list = candidates ?? new List<RetirementCandidate>();
            bool rtl = services.Localization.IsRightToLeft;
            DateTime today = DateTime.Today;

            var byId = services.Employees.GetByCompany(companyId, true).ToDictionary(e => e.Id);
            foreach (RetirementCandidate c in list) // already sorted soonest-first by the service
            {
                if (!byId.TryGetValue(c.EmployeeId, out Employee e)) continue;
                Rows.Add(new RetirementRow(e, c, services, rtl, today));
            }

            CountText = Rows.Count.ToString(CultureInfo.InvariantCulture);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        public string Title { get; }
        public string CountText { get; }
        public ObservableCollection<RetirementRow> Rows { get; } = new ObservableCollection<RetirementRow>();
        public bool IsEmpty => Rows.Count == 0;

        public Action RequestClose { get; set; }
        public ICommand CloseCommand { get; }
    }

    /// <summary>One row of the retirement list.</summary>
    public sealed class RetirementRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public RetirementRow(Employee e, RetirementCandidate c, AppServices services, bool rtl, DateTime today)
        {
            string fr = (e.LastNameFr + " " + e.FirstNameFr).Trim();
            string ar = ((e.LastNameAr ?? string.Empty) + " " + (e.FirstNameAr ?? string.Empty)).Trim();
            Name = rtl && ar.Length > 0 ? ar : (fr.Length > 0 ? fr : ar);

            BirthDate = e.BirthDate.HasValue ? e.BirthDate.Value.ToString("dd/MM/yyyy", Fr) : "—";
            int age = e.BirthDate.HasValue ? AgeAt(e.BirthDate.Value, today) : 0;
            Age = e.BirthDate.HasValue ? string.Format(services.Localization.GetString("Workforce_AgeYears"), age.ToString(Fr)) : "—";
            RetirementDate = c.RetirementDate.ToString("dd/MM/yyyy", Fr);
            Status = services.Localization.GetString(c.AlreadyReached ? "Workforce_Retirement_StatusPassed" : "Workforce_Retirement_StatusSoon");
        }

        public string Name { get; }
        public string BirthDate { get; }
        public string Age { get; }
        public string RetirementDate { get; }
        public string Status { get; }

        private static int AgeAt(DateTime birth, DateTime on)
        {
            int age = on.Year - birth.Year;
            if (birth.Date > on.AddYears(-age)) age--;
            return age < 0 ? 0 : age;
        }
    }
}
