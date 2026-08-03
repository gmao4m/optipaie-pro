using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// DAC tab (tranche 2a) — the read-only Déclaration d'Assiette de Cotisation recap the
    /// accountant reads from to fill the CNAS portal by hand. It shows the period assiette, the
    /// cotisations at the app's configured rates, the official branch split (read-only, "à
    /// confirmer"), and the applied-vs-official gap. Every figure has a copy button. Reads only.
    /// </summary>
    public sealed class CnasDacViewModel : ObservableObject, IActivable
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;

        private int _year = DateTime.Today.Year;
        private CnasCadenceOption _cadence;
        private CnasPeriodOption _period;
        private bool _suspend;

        private bool _hasCompany, _hasData, _employerIssue;
        private string _cadenceInfo = string.Empty, _periodLabel = string.Empty, _statusMessage = string.Empty;
        private string _employerNumber = string.Empty, _employerNumberCopy = string.Empty, _employerMessage = string.Empty;
        private string _effectif = string.Empty, _effectifCopy = string.Empty;
        private string _assiette = string.Empty, _assietteCopy = string.Empty;
        private string _salariale = string.Empty, _salarialeCopy = string.Empty, _salarialeRate = string.Empty;
        private string _patronale = string.Empty, _patronaleCopy = string.Empty, _patronaleRate = string.Empty;
        private string _total = string.Empty, _totalCopy = string.Empty;
        private string _officialPatronale = string.Empty, _officialPatronaleCopy = string.Empty, _officialPatronaleRate = string.Empty;
        private bool _hasGap;
        private string _gapDa = string.Empty, _gapPoints = string.Empty;
        private bool _hasRoundingWarning;
        private string _roundingWarning = string.Empty;
        private bool _salarialeWithheldVisible, _patronaleWithheldVisible;
        private string _salarialeWithheldLine = string.Empty, _patronaleWithheldLine = string.Empty;
        private bool _hasMovements;

        public CnasDacViewModel(AppServices services)
        {
            _services = services;
            OpenFicheCommand = new RelayCommand(OpenFiche);
            for (int y = DateTime.Today.Year; y >= DateTime.Today.Year - 5; y--) Years.Add(y);

            Cadences.Add(new CnasCadenceOption(CnasCadenceMode.Auto, L("Cnas_Cadence_Auto")));
            Cadences.Add(new CnasCadenceOption(CnasCadenceMode.Monthly, L("Cnas_Cadence_Monthly")));
            Cadences.Add(new CnasCadenceOption(CnasCadenceMode.Quarterly, L("Cnas_Cadence_Quarterly")));
            _cadence = Cadences[0];
        }

        /// <summary>Entrées/sorties de la période — l'annexe mouvements à recopier sur le portail.</summary>
        public ObservableCollection<CnasMovementItem> Movements { get; } = new ObservableCollection<CnasMovementItem>();

        /// <summary>Opens an employee's 360° fiche (a movement row is clicked). Read-only for CNAS.</summary>
        public ICommand OpenFicheCommand { get; }

        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();
        public ObservableCollection<CnasCadenceOption> Cadences { get; } = new ObservableCollection<CnasCadenceOption>();
        public ObservableCollection<CnasPeriodOption> Periods { get; } = new ObservableCollection<CnasPeriodOption>();

        /// <summary>The official (décret 94-187) branch split — display only, "à confirmer".</summary>
        public ObservableCollection<CnasBranchRow> Branches { get; } = new ObservableCollection<CnasBranchRow>();

        public int SelectedYear { get => _year; set { if (Set(ref _year, value)) Reload(); } }

        public CnasCadenceOption SelectedCadence
        {
            get => _cadence;
            set
            {
                if (Set(ref _cadence, value) && value != null && !_suspend)
                {
                    Company c = _services.CompanyContext.Active;
                    if (c != null) _services.Settings.Set(CadenceKey(c.Id), value.Mode.ToString());
                    Reload();
                }
            }
        }

        public CnasPeriodOption SelectedPeriod { get => _period; set { if (Set(ref _period, value) && !_suspend) Recompute(); } }

        public bool HasCompany { get => _hasCompany; private set => Set(ref _hasCompany, value); }
        public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public string CadenceInfo { get => _cadenceInfo; private set => Set(ref _cadenceInfo, value); }
        public string PeriodLabel { get => _periodLabel; private set => Set(ref _periodLabel, value); }

        public bool EmployerNumberIssue { get => _employerIssue; private set => Set(ref _employerIssue, value); }
        public string EmployerMessage { get => _employerMessage; private set => Set(ref _employerMessage, value); }
        public string EmployerNumber { get => _employerNumber; private set => Set(ref _employerNumber, value); }
        public string EmployerNumberCopy { get => _employerNumberCopy; private set => Set(ref _employerNumberCopy, value); }

        public string Effectif { get => _effectif; private set => Set(ref _effectif, value); }
        public string EffectifCopy { get => _effectifCopy; private set => Set(ref _effectifCopy, value); }
        public string Assiette { get => _assiette; private set => Set(ref _assiette, value); }
        public string AssietteCopy { get => _assietteCopy; private set => Set(ref _assietteCopy, value); }

        public string SalarialeRate { get => _salarialeRate; private set => Set(ref _salarialeRate, value); }
        public string Salariale { get => _salariale; private set => Set(ref _salariale, value); }
        public string SalarialeCopy { get => _salarialeCopy; private set => Set(ref _salarialeCopy, value); }
        public string PatronaleRate { get => _patronaleRate; private set => Set(ref _patronaleRate, value); }
        public string Patronale { get => _patronale; private set => Set(ref _patronale, value); }
        public string PatronaleCopy { get => _patronaleCopy; private set => Set(ref _patronaleCopy, value); }
        public string Total { get => _total; private set => Set(ref _total, value); }
        public string TotalCopy { get => _totalCopy; private set => Set(ref _totalCopy, value); }

        public string OfficialPatronaleRate { get => _officialPatronaleRate; private set => Set(ref _officialPatronaleRate, value); }
        public string OfficialPatronale { get => _officialPatronale; private set => Set(ref _officialPatronale, value); }
        public string OfficialPatronaleCopy { get => _officialPatronaleCopy; private set => Set(ref _officialPatronaleCopy, value); }

        public bool HasGap { get => _hasGap; private set => Set(ref _hasGap, value); }
        public string GapDa { get => _gapDa; private set => Set(ref _gapDa, value); }
        public string GapPoints { get => _gapPoints; private set => Set(ref _gapPoints, value); }

        /// <summary>
        /// True when the displayed (2-decimal) branch amounts do not sum back to the displayed
        /// official total — a pure presentation residue (the underlying figures reconcile exactly).
        /// Surfaced, never silently absorbed: no branch is nudged to hide it.
        /// </summary>
        public bool HasRoundingWarning { get => _hasRoundingWarning; private set => Set(ref _hasRoundingWarning, value); }
        public string RoundingWarning { get => _roundingWarning; private set => Set(ref _roundingWarning, value); }

        /// <summary>
        /// Discreet "declared vs withheld" coherence line, shown only when the sum of the frozen
        /// per-payslip contributions differs from the aggregate figure the DAC declares. It is a
        /// cross-check, never a correction: the declared figure stays the aggregate one, and this
        /// is styled sober (no colour, no alarm) — a displayed centime must not cost worry.
        /// </summary>
        public bool HasSalarialeWithholdingGap { get => _salarialeWithheldVisible; private set => Set(ref _salarialeWithheldVisible, value); }
        public string SalarialeWithheldLine { get => _salarialeWithheldLine; private set => Set(ref _salarialeWithheldLine, value); }
        public bool HasPatronaleWithholdingGap { get => _patronaleWithheldVisible; private set => Set(ref _patronaleWithheldVisible, value); }
        public string PatronaleWithheldLine { get => _patronaleWithheldLine; private set => Set(ref _patronaleWithheldLine, value); }

        public bool HasMovements { get => _hasMovements; private set => Set(ref _hasMovements, value); }

        public void OnActivated() => Reload();

        private string L(string key) => _services.Localization.GetString(key);
        private static string CadenceKey(long companyId) => "Cnas.Cadence." + companyId;

        private void Reload()
        {
            Company company = _services.CompanyContext.Active;
            HasCompany = company != null;
            if (!HasCompany)
            {
                HasData = false;
                StatusMessage = L("Cnas_NoCompany");
                return;
            }

            _suspend = true;

            // Cadence: restore the per-company preference, then resolve Auto by headcount.
            string saved = _services.Settings.Get(CadenceKey(company.Id), CnasCadenceMode.Auto.ToString());
            _cadence = Cadences.FirstOrDefault(c => c.Mode.ToString() == saved) ?? Cadences[0];
            Raise(nameof(SelectedCadence));

            bool quarterly = EffectiveCadence(company.Id) == CnasCadenceMode.Quarterly;
            CadenceInfo = _cadence.Mode == CnasCadenceMode.Auto
                ? string.Format(L("Cnas_CadenceAutoInfo"), L(quarterly ? "Cnas_Cadence_Quarterly" : "Cnas_Cadence_Monthly"))
                : string.Empty;

            RebuildPeriods(quarterly);
            _suspend = false;

            Recompute();
        }

        private CnasCadenceMode EffectiveCadence(long companyId)
        {
            if (_cadence.Mode == CnasCadenceMode.Monthly) return CnasCadenceMode.Monthly;
            if (_cadence.Mode == CnasCadenceMode.Quarterly) return CnasCadenceMode.Quarterly;
            // Auto: >= 10 salariés actifs → mensuelle ; sinon trimestrielle.
            return _services.Employees.GetByCompany(companyId, false).Count >= 10
                ? CnasCadenceMode.Monthly
                : CnasCadenceMode.Quarterly;
        }

        private void RebuildPeriods(bool quarterly)
        {
            Periods.Clear();
            if (quarterly)
            {
                for (int q = 1; q <= 4; q++)
                {
                    int start = (q - 1) * 3 + 1;
                    Periods.Add(new CnasPeriodOption("T" + q, new[] { start, start + 1, start + 2 }));
                }
                int curQ = (DateTime.Today.Month - 1) / 3;
                _period = Periods[Math.Min(curQ, 3)];
            }
            else
            {
                for (int m = 1; m <= 12; m++) Periods.Add(new CnasPeriodOption(MonthName(m), new[] { m }));
                _period = Periods[Math.Max(0, DateTime.Today.Month - 1)];
            }
            Raise(nameof(SelectedPeriod));
        }

        private void Recompute()
        {
            Company company = _services.CompanyContext.Active;
            if (company == null || _period == null) return;

            CnasDacReport d = _services.CnasDeclarations.BuildDac(company.Id, _year, _period.Months);

            PeriodLabel = _period.Label + " " + _year;
            EmployerNumberIssue = d.EmployerNumberMissing || d.EmployerNumberMalformed;
            EmployerMessage = d.EmployerNumberMissing ? L("Cnas_EmployerMissing")
                : d.EmployerNumberMalformed ? L("Cnas_EmployerMalformed") : string.Empty;
            EmployerNumber = string.IsNullOrEmpty(d.CnasEmployerNumber) ? "—" : d.CnasEmployerNumber;
            EmployerNumberCopy = new string((d.CnasEmployerNumber ?? string.Empty).Where(char.IsDigit).ToArray());

            HasData = d.Effectif > 0;
            StatusMessage = HasData ? string.Empty : L("Cnas_Dac_NoData");

            Effectif = d.Effectif.ToString(Fr);
            EffectifCopy = d.Effectif.ToString(CultureInfo.InvariantCulture);
            Assiette = Money(d.Assiette); AssietteCopy = Copy(d.Assiette);

            SalarialeRate = Pct(d.RateSalariale);
            Salariale = Money(d.CotisationSalariale); SalarialeCopy = Copy(d.CotisationSalariale);
            PatronaleRate = Pct(d.RatePatronale);
            Patronale = Money(d.CotisationPatronale); PatronaleCopy = Copy(d.CotisationPatronale);
            Total = Money(d.CotisationTotale); TotalCopy = Copy(d.CotisationTotale);

            // Contrôle discret « déclaré vs retenu ». The DAC declares the aggregate figure
            // Round2(assiette × taux); payroll withheld Round2(base × taux) per bulletin and froze
            // it. When those totals differ (per-slip rounding, grows with headcount), show a sober
            // one-liner — the declared figure is untouched, this is coherence only, never a fix.
            decimal salGap = d.FrozenCnasEmployee - Round2(d.CotisationSalariale);
            HasSalarialeWithholdingGap = salGap != 0m;
            SalarialeWithheldLine = HasSalarialeWithholdingGap
                ? string.Format(L("Cnas_Dac_WithheldLine"), Money(d.FrozenCnasEmployee), SignedMoney(salGap))
                : string.Empty;

            decimal patGap = d.FrozenCnasEmployer - Round2(d.CotisationPatronale);
            HasPatronaleWithholdingGap = patGap != 0m;
            PatronaleWithheldLine = HasPatronaleWithholdingGap
                ? string.Format(L("Cnas_Dac_WithheldLine"), Money(d.FrozenCnasEmployer), SignedMoney(patGap))
                : string.Empty;

            OfficialPatronaleRate = Pct(d.OfficialRatePatronale);
            OfficialPatronale = Money(d.OfficialCotisationPatronale);
            OfficialPatronaleCopy = Copy(d.OfficialCotisationPatronale);

            HasGap = d.HasRateGap;
            GapDa = Money(d.EcartPatronaleDa);
            GapPoints = d.EcartPatronalePoints.ToString("0.##", Fr) + " " + L("Cnas_Points");

            Branches.Clear();
            foreach (CnasContributionBranch b in d.OfficialBranches)
            {
                Branches.Add(new CnasBranchRow(
                    b.Name, Pct(b.PatronaleRate), b.SalarialeRate > 0 ? Pct(b.SalarialeRate) : "—",
                    Money(b.PatronaleAmount), Copy(b.PatronaleAmount),
                    b.SalarialeRate > 0 ? Money(b.SalarialeAmount) : "—", Copy(b.SalarialeAmount)));
            }

            // Rounding-robustness check (display only): every figure rounds to the centime, so a
            // sum of rounded lines can differ from its rounded total by a centime or two. We
            // surface the largest such residue — we never nudge a figure to make it vanish.
            // Three places it can show: the applied total vs its two lines, and each side of the
            // official branch split vs its total.
            decimal residueApplied = Round2(d.CotisationTotale) - (Round2(d.CotisationSalariale) + Round2(d.CotisationPatronale));
            decimal residuePat = Round2(d.OfficialCotisationPatronale) - d.OfficialBranches.Sum(b => Round2(b.PatronaleAmount));
            decimal residueSal = Round2(d.OfficialCotisationSalariale) - d.OfficialBranches.Sum(b => Round2(b.SalarialeAmount));
            decimal maxResidue = Math.Max(Math.Abs(residueApplied), Math.Max(Math.Abs(residuePat), Math.Abs(residueSal)));
            HasRoundingWarning = maxResidue != 0m;
            RoundingWarning = HasRoundingWarning
                ? string.Format(L("Cnas_Dac_RoundingWarning"), maxResidue.ToString("0.00", Fr))
                : string.Empty;

            Movements.Clear();
            foreach (CnasMovementRow m in _services.CnasDeclarations.BuildMovements(company.Id, _year, _period.Months))
            {
                Movements.Add(new CnasMovementItem(
                    m.EmployeeId, m.IsEntry,
                    L(m.IsEntry ? "Cnas_Mov_Entry" : "Cnas_Mov_Exit"),
                    (m.LastName + " " + m.FirstName).Trim(),
                    string.IsNullOrEmpty(m.Nss) ? "—" : m.Nss,
                    new string((m.Nss ?? string.Empty).Where(char.IsDigit).ToArray()),
                    m.Date.ToString("dd/MM/yyyy", Fr),
                    m.Date.ToString("ddMMyyyy", Fr)));
            }
            HasMovements = Movements.Count > 0;
        }

        private void OpenFiche(object parameter)
        {
            if (!(parameter is CnasMovementItem row) || row.EmployeeId <= 0) return;
            Dialogs.ShowEmployeeProfile(new EmployeeProfileViewModel(_services, row.EmployeeId, null));
        }

        // Half-away-from-zero, matching the "N2" display rounding on net48.
        private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        private static string Money(decimal v) => v.ToString("N2", Fr) + " DA";
        private static string SignedMoney(decimal v) => (v > 0m ? "+" : v < 0m ? "−" : string.Empty) + Money(Math.Abs(v));
        private static string Copy(decimal v) => v.ToString("0.00", Fr);
        private static string Pct(decimal rate) => (rate * 100m).ToString("0.###", Fr) + " %";

        private static string MonthName(int m)
        {
            string name = Fr.DateTimeFormat.GetMonthName(m);
            return char.ToUpper(name[0], Fr) + name.Substring(1);
        }
    }

    public enum CnasCadenceMode { Auto, Monthly, Quarterly }

    public sealed class CnasCadenceOption
    {
        public CnasCadenceOption(CnasCadenceMode mode, string label) { Mode = mode; Label = label; }
        public CnasCadenceMode Mode { get; }
        public string Label { get; }
    }

    public sealed class CnasPeriodOption
    {
        public CnasPeriodOption(string label, IReadOnlyList<int> months) { Label = label; Months = months; }
        public string Label { get; }
        public IReadOnlyList<int> Months { get; }
    }

    /// <summary>One official-split branch row (rates + amounts, with copy strings for each amount).</summary>
    public sealed class CnasBranchRow
    {
        public CnasBranchRow(string name, string patRate, string salRate, string patAmount, string patCopy, string salAmount, string salCopy)
        {
            Name = name; PatronaleRate = patRate; SalarialeRate = salRate;
            PatronaleAmount = patAmount; PatronaleCopy = patCopy; SalarialeAmount = salAmount; SalarialeCopy = salCopy;
        }

        public string Name { get; }
        public string PatronaleRate { get; }
        public string SalarialeRate { get; }
        public string PatronaleAmount { get; }
        public string PatronaleCopy { get; }
        public string SalarialeAmount { get; }
        public string SalarialeCopy { get; }
    }

    /// <summary>One movements-annex row (entrée/sortie) with display + copy strings and the fiche id.</summary>
    public sealed class CnasMovementItem
    {
        public CnasMovementItem(long employeeId, bool isEntry, string typeLabel, string name,
            string nss, string nssCopy, string dateText, string dateCopy)
        {
            EmployeeId = employeeId; IsEntry = isEntry; TypeLabel = typeLabel; Name = name;
            Nss = nss; NssCopy = nssCopy; DateText = dateText; DateCopy = dateCopy;
        }

        public long EmployeeId { get; }
        public bool IsEntry { get; }
        public string TypeLabel { get; }
        public string Name { get; }
        public string Nss { get; }
        public string NssCopy { get; }
        public string DateText { get; }
        public string DateCopy { get; }
    }
}
