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

        private DateTime _stoppageDate = DateTime.Today;
        private int _numberOfDays = 15;
        private bool _hasResumedWork;

        // Printer calibration for the pre-printed form: a global X/Y shift (mm) memorised per printer.
        private string _selectedPrinter;
        private decimal _offsetX, _offsetY;
        private bool _loadingOffsets;

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
            PrintCalibrationCommand = new RelayCommand(PrintCalibration);
        }

        // ── selection ────────────────────────────────────────────────────────
        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<ContributionRow> Contributions { get; } = new ObservableCollection<ContributionRow>();
        public ObservableCollection<string> Printers { get; } = new ObservableCollection<string>();

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
        public ICommand PrintCalibrationCommand { get; }

        // ── printer calibration (pre-printed form alignment) ─────────────────
        public string SelectedPrinter
        {
            get => _selectedPrinter;
            set { if (Set(ref _selectedPrinter, value)) LoadOffsets(); }
        }

        /// <summary>Global horizontal shift in mm applied to every printed value (per printer).</summary>
        public decimal OffsetX { get => _offsetX; set { if (Set(ref _offsetX, value)) SaveOffsets(); } }
        /// <summary>Global vertical shift in mm applied to every printed value (per printer).</summary>
        public decimal OffsetY { get => _offsetY; set { if (Set(ref _offsetY, value)) SaveOffsets(); } }

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

            LoadPrinters();
        }

        // ── printer calibration ──────────────────────────────────────────────
        private void LoadPrinters()
        {
            if (Printers.Count > 0) return;
            string current = null;
            try
            {
                foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                    Printers.Add(p);
                current = new System.Drawing.Printing.PrinterSettings().PrinterName;
            }
            catch { /* headless / no print subsystem — offsets still work via the default bucket */ }

            if (Printers.Count == 0) Printers.Add("(imprimante par défaut)");
            SelectedPrinter = !string.IsNullOrEmpty(current) && Printers.Contains(current) ? current : Printers[0];
        }

        private static string OffsetKey(string printer, char axis) =>
            "ats.offset." + (string.IsNullOrEmpty(printer) ? "default" : printer.Replace('.', '_')) + "." + axis;

        private void LoadOffsets()
        {
            _loadingOffsets = true;
            try
            {
                _offsetX = ParseMm(_services.Settings.Get(OffsetKey(_selectedPrinter, 'x'), "0"));
                _offsetY = ParseMm(_services.Settings.Get(OffsetKey(_selectedPrinter, 'y'), "0"));
                Raise(nameof(OffsetX));
                Raise(nameof(OffsetY));
            }
            finally { _loadingOffsets = false; }
        }

        private void SaveOffsets()
        {
            if (_loadingOffsets || string.IsNullOrEmpty(_selectedPrinter)) return;
            _services.Settings.Set(OffsetKey(_selectedPrinter, 'x'), _offsetX.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _services.Settings.Set(OffsetKey(_selectedPrinter, 'y'), _offsetY.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static decimal ParseMm(string s) =>
            OptiPaie.Common.Text.FlexibleNumber.TryParse(s, out decimal v) ? v : 0m;

        private void PrintCalibration()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "OptiPaie_mire_calage.pdf");
                _services.AtsDrtDocuments.GenerateCalibrationSheet((double)_offsetX, (double)_offsetY, path);
                SetStatus("Mire de calage générée : " + path, false);
                TryOpen(path);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Calibration sheet failed.", ex);
                SetStatus("Échec de la mire de calage : " + ex.Message, true);
            }
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
                _stoppageDate.ToString("ddMMyy")).Replace(" ", "_") + ".pdf";
            foreach (char c in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(c, '_');

            var dialog = new SaveFileDialog { Filter = "Document PDF (*.pdf)|*.pdf", FileName = suggested };
            if (dialog.ShowDialog() != true) return;

            try
            {
                // The PDF carries ONLY the values, placed at absolute mm — it prints ON TOP of the
                // blank pre-printed CNAS form. The per-printer offset shifts every value together.
                double ox = (double)_offsetX, oy = (double)_offsetY;
                string pdfPath;
                if (_certType == "ATS")
                {
                    var contribs = new List<Cert.MonthlyContribution>();
                    foreach (ContributionRow r in Contributions) contribs.Add(r.ToModel());
                    pdfPath = _services.AtsDrtDocuments.GenerateAts(
                        certCompany, certEmployee, stoppage, _hasResumedWork, contribs, weekend, ox, oy, dialog.FileName);
                }
                else
                {
                    pdfPath = _services.AtsDrtDocuments.GenerateDrt(
                        certCompany, certEmployee, stoppage, _hasResumedWork, weekend, ox, oy, dialog.FileName);
                }

                SetStatus("Document généré : " + pdfPath, false);
                TryOpen(pdfPath);
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
            private decimal? _daysWorked, _contributionBase;
            private string _absenceReason;

            public ContributionRow(Cert.MonthlyContribution model)
            {
                MonthLabel = model.MonthLabel;
                IsActive = model.IsActive;
                _daysWorked = model.DaysWorked;
                _absenceReason = model.AbsenceReason;
                _contributionBase = model.ContributionBase;
            }

            public string MonthLabel { get; }
            public bool IsActive { get; }

            /// <summary>"Nombre de jours travaillés" — a day count (not hours).</summary>
            public decimal? DaysWorked { get => _daysWorked; set => Set(ref _daysWorked, value); }
            /// <summary>"Motif absences" — a free-text reason, blank if none.</summary>
            public string AbsenceReason { get => _absenceReason; set => Set(ref _absenceReason, value); }
            public decimal? ContributionBase { get => _contributionBase; set { if (Set(ref _contributionBase, value)) Raise(nameof(EmployeeShare)); } }

            /// <summary>Live employee CNAS share = 9% of the base (same rule as the certificate).</summary>
            public decimal? EmployeeShare => _contributionBase.HasValue
                ? Math.Round(_contributionBase.Value * 0.09m, 2) : (decimal?)null;

            public Cert.MonthlyContribution ToModel() => new Cert.MonthlyContribution
            {
                MonthLabel = MonthLabel,
                IsActive = IsActive,
                DaysWorked = _daysWorked,
                AbsenceReason = _absenceReason,
                ContributionBase = _contributionBase
            };
        }
    }
}
