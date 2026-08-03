using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// « Contrôle de préparation CNAS » — a read-only screen that lists the data blockers to
    /// fix before producing a DAC/DAS for the active company and a chosen year. It reads only
    /// (through the read-only CNAS service); it never touches the payroll engine.
    /// </summary>
    public sealed class CnasReadinessViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;

        private int _year = DateTime.Today.Year;
        private bool _hasCompany;
        private bool _employerNumberIssue;
        private string _employerNumberMessage = string.Empty;
        private bool _allReady;
        private string _statusMessage = string.Empty;

        public CnasReadinessViewModel(AppServices services)
        {
            _services = services;
            OpenFicheCommand = new RelayCommand(OpenFiche);
            for (int y = DateTime.Today.Year; y >= DateTime.Today.Year - 5; y--)
            {
                Years.Add(y);
            }
        }

        /// <summary>Opens an employee's 360° fiche when a blocker row is clicked. Read-only for CNAS.</summary>
        public ICommand OpenFicheCommand { get; }

        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();

        /// <summary>Only the employees with at least one blocking issue (the work-list).</summary>
        public ObservableCollection<CnasReadinessRow> Rows { get; } = new ObservableCollection<CnasReadinessRow>();

        public int SelectedYear
        {
            get => _year;
            set { if (Set(ref _year, value)) Load(); }
        }

        public bool HasCompany { get => _hasCompany; private set => Set(ref _hasCompany, value); }
        public bool EmployerNumberIssue { get => _employerNumberIssue; private set => Set(ref _employerNumberIssue, value); }
        public string EmployerNumberMessage { get => _employerNumberMessage; private set => Set(ref _employerNumberMessage, value); }
        public bool AllReady { get => _allReady; private set => Set(ref _allReady, value); }
        public bool HasIssues => Rows.Count > 0;
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public void OnActivated() => Load();

        private string L(string key) => _services.Localization.GetString(key);

        private void Load()
        {
            Rows.Clear();

            Company company = _services.CompanyContext.Active;
            HasCompany = company != null;
            if (!HasCompany)
            {
                EmployerNumberIssue = false;
                AllReady = false;
                StatusMessage = L("Cnas_NoCompany");
                Raise(nameof(HasIssues));
                return;
            }

            CnasReadinessReport report = _services.CnasDeclarations.CheckReadiness(company.Id, _year);

            EmployerNumberIssue = report.EmployerNumberMissing || report.EmployerNumberMalformed;
            EmployerNumberMessage = report.EmployerNumberMissing
                ? L("Cnas_EmployerMissing")
                : report.EmployerNumberMalformed ? L("Cnas_EmployerMalformed") : string.Empty;

            IReadOnlyList<CnasEmployeeReadiness> withIssues = report.EmployeesWithIssues;
            foreach (CnasEmployeeReadiness e in withIssues)
            {
                Rows.Add(new CnasReadinessRow(e.EmployeeId, e.FullName, BuildIssues(e)));
            }

            AllReady = report.IsReady;
            StatusMessage = string.Format(L("Cnas_Summary"), report.ReadyCount, withIssues.Count);
            Raise(nameof(HasIssues));
        }

        private string BuildIssues(CnasEmployeeReadiness e)
        {
            var parts = new List<string>();
            if (e.NssMissing) parts.Add(L("Cnas_Issue_NssMissing"));
            if (e.NssMalformed) parts.Add(L("Cnas_Issue_NssMalformed"));
            if (e.BirthDateMissing) parts.Add(L("Cnas_Issue_BirthMissing"));
            if (e.MonthsBelowSnmg > 0) parts.Add(string.Format(L("Cnas_Issue_BelowSnmg"), e.MonthsBelowSnmg));
            return string.Join("   ·   ", parts);
        }

        private void OpenFiche(object parameter)
        {
            if (!(parameter is CnasReadinessRow row) || row.EmployeeId <= 0) return;
            Dialogs.ShowEmployeeProfile(new EmployeeProfileViewModel(_services, row.EmployeeId, null));
        }
    }

    /// <summary>One row of the readiness work-list: an employee and their concatenated issues.</summary>
    public sealed class CnasReadinessRow
    {
        public CnasReadinessRow(long employeeId, string employee, string issues)
        {
            EmployeeId = employeeId;
            Employee = employee;
            Issues = issues;
        }

        public long EmployeeId { get; }
        public string Employee { get; }
        public string Issues { get; }
    }
}
