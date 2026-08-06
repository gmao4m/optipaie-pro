using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using Cert = OptiPaie.Core.Certificates;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// « Attestations CNAS (ATS · DRT) » — select an employee, auto-fill their identity from
    /// their OptiPaie file, enter the work stoppage (+ the 12-month grid for ATS), and generate
    /// the official CNAS document by filling the real .docx template. The printed document keeps
    /// its official format; only this section's UI carries the emerald/gold identity.
    /// </summary>
    public sealed class AttestationsViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;

        private Company _company;
        private Employee _selectedEmployee;
        private string _certType = "ATS";      // "ATS" | "DRT"
        private bool _drtArabic = true;         // DRT language: Arabic (true) / French (false)

        private DateTime _stoppageDate = DateTime.Today;
        private int _numberOfDays = 15;
        private bool _hasResumedWork;

        // ATS 12-month grid controls.
        private DateTime _gridStartDate = new DateTime(DateTime.Today.Year, 1, 1);
        private int _monthCount = 12;

        // Auto-filled, editable identity (so a missing stored field can be completed inline).
        private string _managerName, _employerNumber, _companyName, _companyAddress, _location;
        private string _empLastName, _empFirstName, _nss, _birthPlace, _position, _empAddress;
        private DateTime? _birthDate, _hireDate;

        private string _status = string.Empty;
        private bool _isError;

        public AttestationsViewModel(AppServices services)
        {
            _services = services;
            GenerateCommand = new RelayCommand(Generate, () => _selectedEmployee != null);
            BuildGridCommand = new RelayCommand(BuildGrid);
        }

        // ── selection ────────────────────────────────────────────────────────
        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<ContributionRow> Contributions { get; } = new ObservableCollection<ContributionRow>();

        public string CompanyDisplay => _company?.NameFr ?? "—";

        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set { if (Set(ref _selectedEmployee, value)) AutoFill(); CommandManager.InvalidateRequerySuggested(); }
        }

        // ── document type ────────────────────────────────────────────────────
        public string CertType
        {
            get => _certType;
            set { if (Set(ref _certType, value)) { Raise(nameof(IsAts)); Raise(nameof(IsDrt)); } }
        }

        public bool IsAts { get => _certType == "ATS"; set { if (value) CertType = "ATS"; } }
        public bool IsDrt { get => _certType == "DRT"; set { if (value) CertType = "DRT"; } }
        public bool DrtArabic { get => _drtArabic; set => Set(ref _drtArabic, value); }

        // ── work stoppage ────────────────────────────────────────────────────
        public DateTime StoppageDate { get => _stoppageDate; set { if (Set(ref _stoppageDate, value)) RaisePreviews(); } }

        // Clamped 0..365 exactly like the source tool's NumericUpDown (a negative count would
        // otherwise print a reprise date earlier than the stoppage on the official certificate).
        public int NumberOfDays { get => _numberOfDays; set { if (Set(ref _numberOfDays, Math.Max(0, Math.Min(365, value)))) RaisePreviews(); } }
        public bool HasResumedWork { get => _hasResumedWork; set { if (Set(ref _hasResumedWork, value)) RaisePreviews(); } }

        // Live feedback of the two computed official dates (Friday+Saturday weekend).
        public string LastWorkedDatePreview =>
            Cert.BusinessDayCalculator.PreviousBusinessDay(_stoppageDate, new Cert.WeekendConfig()).ToString("dd/MM/yyyy");

        public string ResumeDatePreview => !_hasResumedWork ? "—" :
            Cert.BusinessDayCalculator.NextBusinessDayOnOrAfter(_stoppageDate.AddDays(_numberOfDays), new Cert.WeekendConfig())
                .ToString("dd/MM/yyyy");

        // ── ATS grid ─────────────────────────────────────────────────────────
        public DateTime GridStartDate { get => _gridStartDate; set => Set(ref _gridStartDate, value); }
        public int MonthCount { get => _monthCount; set => Set(ref _monthCount, value); }

        // ── identity (auto-filled, editable) ─────────────────────────────────
        public string ManagerName { get => _managerName; set => Set(ref _managerName, value); }
        public string EmployerNumber { get => _employerNumber; set => Set(ref _employerNumber, value); }
        public string CompanyName { get => _companyName; set => Set(ref _companyName, value); }
        public string CompanyAddress { get => _companyAddress; set => Set(ref _companyAddress, value); }
        public string Location { get => _location; set => Set(ref _location, value); }
        public string EmpLastName { get => _empLastName; set => Set(ref _empLastName, value); }
        public string EmpFirstName { get => _empFirstName; set => Set(ref _empFirstName, value); }
        public string Nss { get => _nss; set => Set(ref _nss, value); }
        public DateTime? BirthDate { get => _birthDate; set => Set(ref _birthDate, value); }
        public string BirthPlace { get => _birthPlace; set => Set(ref _birthPlace, value); }
        public string Position { get => _position; set => Set(ref _position, value); }
        public string EmpAddress { get => _empAddress; set => Set(ref _empAddress, value); }
        public DateTime? HireDate { get => _hireDate; set => Set(ref _hireDate, value); }

        public string Status { get => _status; private set => Set(ref _status, value); }
        public bool IsError { get => _isError; private set => Set(ref _isError, value); }

        public ICommand GenerateCommand { get; }
        public ICommand BuildGridCommand { get; }

        // ── lifecycle ────────────────────────────────────────────────────────
        public void OnActivated()
        {
            _company = _services.CompanyContext.Active;
            Raise(nameof(CompanyDisplay));

            Employees.Clear();
            if (_company != null)
                foreach (Employee e in _services.Employees.GetByCompany(_company.Id, false))
                    Employees.Add(e);

            SelectedEmployee = Employees.Count > 0 ? Employees[0] : null;
            if (Contributions.Count == 0) BuildGrid();
        }

        private void AutoFill()
        {
            if (_company != null)
            {
                Company mc = _company;
                ManagerName = mc.ManagerName;
                EmployerNumber = Cert.FormattingHelpers.NormalizeDigits(mc.CnasEmployerNumber);
                CompanyName = mc.NameFr;
                CompanyAddress = mc.AddressFr;
                Location = mc.City;
            }

            Employee e = _selectedEmployee;
            if (e == null) return;

            EmpLastName = e.LastNameFr;
            EmpFirstName = e.FirstNameFr;
            Nss = Cert.FormattingHelpers.NormalizeDigits(e.Nss);
            BirthDate = e.BirthDate;
            BirthPlace = e.BirthPlace;
            Position = e.Poste;
            EmpAddress = e.Address;
            HireDate = e.HireDate;
        }

        private void BuildGrid()
        {
            int count = Math.Min(12, Math.Max(1, _monthCount));
            // Month labels follow the APP UI language (like the source tool), NOT the DRT-only
            // language flag — otherwise a French-UI user would get Arabic months on the ATS form.
            bool arabicMonths = _services.Localization.CurrentLanguage == "ar";
            List<Cert.MonthlyContribution> grid = _services.AtsDrtDocuments.BuildMonthGrid(_gridStartDate, count, arabicMonths);

            // Only the active months are editable in the grid; the mapper emits the unused
            // trailing slots as "/" automatically, so passing just the active rows is correct.
            Contributions.Clear();
            foreach (Cert.MonthlyContribution row in grid)
                if (row.IsActive) Contributions.Add(new ContributionRow(row));
        }

        private void RaisePreviews()
        {
            Raise(nameof(LastWorkedDatePreview));
            Raise(nameof(ResumeDatePreview));
        }

        private void Generate()
        {
            if (_selectedEmployee == null) { SetStatus("Sélectionnez un employé.", true); return; }

            // These print on an official CNAS document — validate strictly, like the source tool.
            if (!Cert.FormattingHelpers.IsValidNss(_nss))
            { SetStatus("N° de sécurité sociale invalide (12 chiffres requis).", true); return; }

            if (_certType == "ATS" && !Cert.FormattingHelpers.IsValidEmployerNumber(_employerNumber))
            { SetStatus("N° d'adhérent CNAS invalide (10 chiffres requis).", true); return; }

            var certCompany = new Cert.Company
            {
                ManagerName = _managerName,
                EmployerNumber = Cert.FormattingHelpers.NormalizeDigits(_employerNumber),
                CompanyName = _companyName,
                Address = _companyAddress,
                Location = _location
            };

            var certEmployee = new Cert.Employee
            {
                LastName = _empLastName,
                FirstName = _empFirstName,
                SocialSecurityNumber = Cert.FormattingHelpers.NormalizeDigits(_nss),
                BirthDate = _birthDate,
                BirthPlace = _birthPlace,
                Position = _position,
                Address = _empAddress,
                HireDate = _hireDate
            };

            var stoppage = new Cert.WorkStoppage { StoppageDate = _stoppageDate, NumberOfDays = _numberOfDays };
            var weekend = new Cert.WeekendConfig();

            string suggested = (_certType + "_" + (_empLastName ?? "") + "_" + _empFirstName + "_" +
                _stoppageDate.ToString("ddMMyy")).Replace(" ", "_") + ".docx";
            foreach (char c in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(c, '_');

            var dialog = new SaveFileDialog { Filter = "Document Word (*.docx)|*.docx", FileName = suggested };
            if (dialog.ShowDialog() != true) return;

            try
            {
                string docxPath;
                if (_certType == "ATS")
                {
                    var contribs = new List<Cert.MonthlyContribution>();
                    foreach (ContributionRow r in Contributions) contribs.Add(r.ToModel());
                    docxPath = _services.AtsDrtDocuments.GenerateAts(
                        certCompany, certEmployee, stoppage, _hasResumedWork, contribs, weekend, dialog.FileName);
                }
                else
                {
                    docxPath = _services.AtsDrtDocuments.GenerateDrt(
                        certCompany, certEmployee, stoppage, _hasResumedWork, _drtArabic, weekend, dialog.FileName);
                }

                string opened = docxPath;
                // Produce a print-ready PDF next to the .docx when Word is available.
                if (_services.AtsDrtDocuments.IsWordAvailable)
                {
                    try
                    {
                        string pdfPath = Path.ChangeExtension(docxPath, ".pdf");
                        _services.AtsDrtDocuments.ConvertToPdf(docxPath, pdfPath);
                        opened = pdfPath;
                    }
                    catch (Exception ex)
                    {
                        _services.Logger.Warn("ATS/DRT PDF export failed: " + ex.Message);
                    }
                }

                SetStatus("Document généré : " + opened, false);
                TryOpen(opened);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("ATS/DRT generation failed.", ex);
                SetStatus("Échec de la génération : " + ex.Message, true);
            }
        }

        private static void TryOpen(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* opening is best-effort; the file is saved regardless */ }
        }

        private void SetStatus(string message, bool isError) { IsError = isError; Status = message; }

        /// <summary>Editable row of the ATS 12-month contribution grid (MSS = 9% live).</summary>
        public sealed class ContributionRow : ObservableObject
        {
            private decimal? _hours, _absence, _contributionBase;

            public ContributionRow(Cert.MonthlyContribution model)
            {
                MonthLabel = model.MonthLabel;
                IsActive = model.IsActive;
                _hours = model.HoursWorked;
                _absence = model.AbsenceDays;
                _contributionBase = model.ContributionBase;
            }

            public string MonthLabel { get; }
            public bool IsActive { get; }

            public decimal? Hours { get => _hours; set => Set(ref _hours, value); }
            public decimal? Absence { get => _absence; set => Set(ref _absence, value); }
            public decimal? ContributionBase { get => _contributionBase; set { if (Set(ref _contributionBase, value)) Raise(nameof(EmployeeShare)); } }

            /// <summary>Live employee CNAS share = 9% of the base (same rule as the certificate).</summary>
            public decimal? EmployeeShare => _contributionBase.HasValue
                ? Math.Round(_contributionBase.Value * 0.09m, 2) : (decimal?)null;

            public Cert.MonthlyContribution ToModel() => new Cert.MonthlyContribution
            {
                MonthLabel = MonthLabel,
                IsActive = IsActive,
                HoursWorked = _hours,
                AbsenceDays = _absence,
                ContributionBase = _contributionBase
            };
        }
    }
}
