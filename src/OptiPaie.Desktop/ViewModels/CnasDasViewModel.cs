using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Services.Cnas;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// DAS tab (étape 9) — generate the two annual DAS text files. It validates BEFORE writing
    /// (nominative, grouped refusal), warns nominatively about estimated durations, writes to a
    /// user-chosen folder with the official file names, re-reads and verifies the bytes, records a
    /// discreet trace, and shows a recap the accountant checks before depositing. Reads only.
    /// </summary>
    public sealed class CnasDasViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;

        private int _year = DateTime.Today.Year;
        private bool _hasCompany, _hasEstimated, _hasRefusal, _hasResult;
        private string _statusMessage = string.Empty, _refusalHeadline = string.Empty;
        private string _resultYear = string.Empty, _resultEffectif = string.Empty, _resultAnnual = string.Empty;
        private string _resultQuarters = string.Empty, _resultFiles = string.Empty;
        private string _lastFolder = string.Empty;

        public CnasDasViewModel(AppServices services)
        {
            _services = services;
            GenerateCommand = new RelayCommand(Generate);
            OpenFicheCommand = new RelayCommand(OpenFiche);
            OpenFolderCommand = new RelayCommand(OpenFolder, () => _lastFolder.Length > 0);
            for (int y = DateTime.Today.Year; y >= DateTime.Today.Year - 5; y--) Years.Add(y);
        }

        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<CnasDasEstimatedItem> EstimatedEmployees { get; } = new ObservableCollection<CnasDasEstimatedItem>();
        public ObservableCollection<CnasDasBlockerGroup> BlockerGroups { get; } = new ObservableCollection<CnasDasBlockerGroup>();

        public ICommand GenerateCommand { get; }
        public ICommand OpenFicheCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public int SelectedYear { get => _year; set { if (Set(ref _year, value)) Reload(); } }

        public bool HasCompany { get => _hasCompany; private set => Set(ref _hasCompany, value); }
        public bool HasEstimatedDurations { get => _hasEstimated; private set => Set(ref _hasEstimated, value); }
        public bool HasRefusal { get => _hasRefusal; private set => Set(ref _hasRefusal, value); }
        public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public string RefusalHeadline { get => _refusalHeadline; private set => Set(ref _refusalHeadline, value); }

        public string ResultYear { get => _resultYear; private set => Set(ref _resultYear, value); }
        public string ResultEffectif { get => _resultEffectif; private set => Set(ref _resultEffectif, value); }
        public string ResultAnnual { get => _resultAnnual; private set => Set(ref _resultAnnual, value); }
        public string ResultQuarters { get => _resultQuarters; private set => Set(ref _resultQuarters, value); }
        public string ResultFiles { get => _resultFiles; private set => Set(ref _resultFiles, value); }

        public void OnActivated() => Reload();

        private string L(string key) => _services.Localization.GetString(key);

        private void Reload()
        {
            Company company = _services.CompanyContext.Active;
            HasCompany = company != null;
            HasRefusal = false;
            HasResult = false;
            BlockerGroups.Clear();
            EstimatedEmployees.Clear();
            if (!HasCompany)
            {
                HasEstimatedDurations = false;
                StatusMessage = L("Cnas_NoCompany");
                return;
            }

            StatusMessage = string.Empty;

            // Nominative "durée estimée" list — the salariés whose hours were not measured.
            CnasDasReport das = _services.CnasDeclarations.BuildDas(company.Id, _year);
            foreach (CnasDasEmployee e in das.Employees.Where(x => x.HasEstimatedDuration))
            {
                EstimatedEmployees.Add(new CnasDasEstimatedItem(e.EmployeeId, (e.LastName + " " + e.FirstName).Trim()));
            }
            HasEstimatedDurations = EstimatedEmployees.Count > 0;
        }

        private void Generate()
        {
            Company company = _services.CompanyContext.Active;
            if (company == null) return;

            HasRefusal = false;
            HasResult = false;

            DasExportValidation validation = _services.CnasDeclarations.ValidateDasForExport(company.Id, _year);
            if (!validation.IsValid)
            {
                ShowRefusal(validation);
                return;
            }

            CnasDasReport das = _services.CnasDeclarations.BuildDas(company.Id, _year);
            string employer = new string((das.EmployerNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            string yy = (_year % 100).ToString("00", CultureInfo.InvariantCulture);
            string headerName = "D" + yy + "E" + employer + ".TXT";
            string detailName = "D" + yy + "S" + employer + ".TXT";

            string folder = AskFolder(headerName);
            if (folder == null) return;

            string headerPath = Path.Combine(folder, headerName);
            string detailPath = Path.Combine(folder, detailName);
            try
            {
                string centre = _services.Settings.Get("Cnas.CentrePayeur." + company.Id, string.Empty);
                DasFiles files = new DasFileBuilder().Build(das, new DasHeaderOptions("N", centre, string.Empty));
                File.WriteAllBytes(headerPath, files.HeaderBytes);
                File.WriteAllBytes(detailPath, files.DetailBytes);

                // Re-read the written bytes and verify structure; a corrupt deposit is deleted.
                if (!DasStructure.Verify(File.ReadAllBytes(headerPath), File.ReadAllBytes(detailPath), out string reason))
                {
                    TryDelete(headerPath);
                    TryDelete(detailPath);
                    Dialogs.Error(L("Cnas_Das_VerifyFailed") + " " + reason);
                    return;
                }
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Écriture DAS", ex);
                Dialogs.Error(L("Cnas_Das_WriteError") + " " + ex.Message);
                return;
            }

            RecordTrace(company.Id, das, folder);
            ShowResult(das, folder, headerName, detailName);
        }

        private string AskFolder(string headerName)
        {
            var dialog = new SaveFileDialog
            {
                Title = L("Cnas_Das_ChooseFolder"),
                Filter = "Fichiers DAS (*.TXT)|*.TXT",
                FileName = headerName
            };
            return dialog.ShowDialog() == true ? Path.GetDirectoryName(dialog.FileName) : null;
        }

        private void RecordTrace(long companyId, CnasDasReport das, string folder)
        {
            // Discreet trace via the existing settings store (no migration): if a deposit is
            // ever rejected, we know exactly what was produced and when.
            string key = "Cnas.Das.Log." + companyId;
            string existing = _services.Settings.Get(key, string.Empty);
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + " | " + das.Year + " | " + das.WorkerCount + " salariés | "
                + das.AnnualTotal.ToString("N2", Fr) + " DA | " + folder;
            _services.Settings.Set(key, existing.Length == 0 ? line : existing + "\n" + line);
        }

        private void ShowResult(CnasDasReport das, string folder, string headerName, string detailName)
        {
            ResultYear = das.Year.ToString(CultureInfo.InvariantCulture);
            ResultEffectif = das.WorkerCount.ToString(Fr);
            ResultAnnual = das.AnnualTotal.ToString("N2", Fr) + " DA";
            ResultQuarters = string.Join("    ·    ", das.QuarterTotals.Select((t, i) => "T" + (i + 1) + " " + t.ToString("N2", Fr)));
            ResultFiles = Path.Combine(folder, headerName) + Environment.NewLine + Path.Combine(folder, detailName);
            _lastFolder = folder;
            HasResult = true;
        }

        private void ShowRefusal(DasExportValidation validation)
        {
            BlockerGroups.Clear();
            // Group by problem type; employee-level groups first (actionable), then company, then structural.
            foreach (IGrouping<DasBlockerType, DasBlocker> g in validation.Blockers
                .GroupBy(b => b.Type)
                .OrderBy(g => TypeOrder(g.Key)))
            {
                var rows = g.Where(b => b.IsEmployeeLevel)
                    .Select(b => new CnasDasBlockerRow(b.EmployeeId, b.EmployeeName + (b.Detail.Length > 0 ? "  (" + b.Detail + ")" : string.Empty)))
                    .ToList();
                BlockerGroups.Add(new CnasDasBlockerGroup(L("Cnas_Das_Blk_" + g.Key), g.Count(), rows));
            }
            RefusalHeadline = string.Format(L("Cnas_Das_RefusalHeadline"), validation.EmployeesToFix);
            HasRefusal = true;
        }

        private static int TypeOrder(DasBlockerType t)
        {
            switch (t)
            {
                case DasBlockerType.NssMissing:
                case DasBlockerType.NssMalformed:
                case DasBlockerType.BirthDateMissing:
                case DasBlockerType.NameNotAscii:
                case DasBlockerType.AmountExceedsField:
                    return 0; // employee-fixable first
                case DasBlockerType.EmployerNumberMissing:
                case DasBlockerType.EmployerNumberMalformed:
                    return 1; // company-level
                default:
                    return 2; // structural / support
            }
        }

        private void OpenFiche(object parameter)
        {
            long id = parameter is CnasDasEstimatedItem est ? est.EmployeeId
                : parameter is CnasDasBlockerRow row ? row.EmployeeId : 0;
            if (id <= 0) return;
            Dialogs.ShowEmployeeProfile(new EmployeeProfileViewModel(_services, id, null));
        }

        private void OpenFolder()
        {
            if (_lastFolder.Length == 0) return;
            try { Process.Start(new ProcessStartInfo(_lastFolder) { UseShellExecute = true }); }
            catch (Exception ex) { _services.Logger.Warn("Ouverture du dossier impossible : " + ex.Message); }
        }

        private void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>A salarié whose DAS duration is estimated (not measured) — clickable to the fiche.</summary>
    public sealed class CnasDasEstimatedItem
    {
        public CnasDasEstimatedItem(long employeeId, string name) { EmployeeId = employeeId; Name = name; }
        public long EmployeeId { get; }
        public string Name { get; }
    }

    /// <summary>A group of blockers of one type: a localized title, a count, and the employee rows.</summary>
    public sealed class CnasDasBlockerGroup
    {
        public CnasDasBlockerGroup(string title, int count, IReadOnlyList<CnasDasBlockerRow> rows)
        {
            Title = title; Count = count; Rows = rows;
        }
        public string Title { get; }
        public int Count { get; }
        public IReadOnlyList<CnasDasBlockerRow> Rows { get; }
        public bool HasRows => Rows.Count > 0;
    }

    /// <summary>One clickable blocker row: an employee to correct.</summary>
    public sealed class CnasDasBlockerRow
    {
        public CnasDasBlockerRow(long employeeId, string name) { EmployeeId = employeeId; Name = name; }
        public long EmployeeId { get; }
        public string Name { get; }
    }
}
