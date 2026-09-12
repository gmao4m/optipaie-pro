using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The workforce (effectif) section of the dashboard: key figures, a single switchable
    /// distribution panel and the department/poste structure — every figure clickable to the
    /// nominative list. Pure presentation; all numbers come from the tested
    /// <see cref="Core.Interfaces.Services.IDashboardService.BuildWorkforce"/>, never the engine.
    /// </summary>
    public sealed class WorkforceViewModel : ObservableObject
    {
        private const double BarMax = 160.0;
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private WorkforceAnalytics _wa;
        private PeriodOption _period;
        private AxisOption _axis;

        private string _headcount = "0", _avgAge = "—", _avgAgeNote = string.Empty;
        private string _entries = "0", _exits = "0", _turnover = "0 %", _turnoverRaw = string.Empty, _turnoverTip = string.Empty;
        private string _asOf = string.Empty, _periodLabel = string.Empty;
        private bool _currentUnreliable;

        public WorkforceViewModel(AppServices services)
        {
            _services = services;

            Periods.Add(new PeriodOption("month", L("Workforce_Period_Month")));
            Periods.Add(new PeriodOption("quarter", L("Workforce_Period_Quarter")));
            Periods.Add(new PeriodOption("year", L("Workforce_Period_Year")));
            _period = Periods[0];

            Axes.Add(new AxisOption("contract", L("Workforce_Axis_Contract"), w => w.ByContract));
            Axes.Add(new AxisOption("category", L("Workforce_Axis_Category"), w => w.ByCategory));
            Axes.Add(new AxisOption("marital", L("Workforce_Axis_Marital"), w => w.ByMaritalStatus));
            Axes.Add(new AxisOption("gender", L("Workforce_Axis_Gender"), w => w.ByGender));
            Axes.Add(new AxisOption("age", L("Workforce_Axis_Age"), w => w.ByAgeBand));
            Axes.Add(new AxisOption("seniority", L("Workforce_Axis_Seniority"), w => w.BySeniority));
            _axis = Axes[0];

            OpenBucketCommand = new RelayCommand(p => OpenBucket(p as BucketVM));
            OpenHeadcountCommand = new RelayCommand(() => OpenList(L("Workforce_Headcount"), _wa?.HeadcountIds));
            OpenEntriesCommand = new RelayCommand(() => OpenList(L("Workforce_Entries"), _wa?.EntryIds));
            OpenExitsCommand = new RelayCommand(() => OpenList(L("Workforce_Exits"), _wa?.ExitIds));
        }

        // ── period ──
        public ObservableCollection<PeriodOption> Periods { get; } = new ObservableCollection<PeriodOption>();
        public PeriodOption SelectedPeriod
        {
            get => _period;
            set { if (Set(ref _period, value) && value != null) Load(); }
        }

        // ── key figures ──
        public string HeadcountText { get => _headcount; private set => Set(ref _headcount, value); }
        public string AverageAgeText { get => _avgAge; private set => Set(ref _avgAge, value); }
        public string AverageAgeNote { get => _avgAgeNote; private set => Set(ref _avgAgeNote, value); }
        public string EntriesText { get => _entries; private set => Set(ref _entries, value); }
        public string ExitsText { get => _exits; private set => Set(ref _exits, value); }
        public string TurnoverText { get => _turnover; private set => Set(ref _turnover, value); }
        public string TurnoverRaw { get => _turnoverRaw; private set => Set(ref _turnoverRaw, value); }
        public string TurnoverTooltip { get => _turnoverTip; private set => Set(ref _turnoverTip, value); }
        public string AsOfText { get => _asOf; private set => Set(ref _asOf, value); }
        public string PeriodLabel { get => _periodLabel; private set => Set(ref _periodLabel, value); }

        // ── distribution panel ──
        public ObservableCollection<AxisOption> Axes { get; } = new ObservableCollection<AxisOption>();
        public AxisOption SelectedAxis
        {
            get => _axis;
            set { if (Set(ref _axis, value) && value != null) RebuildCurrent(); }
        }
        public ObservableCollection<BucketVM> CurrentBuckets { get; } = new ObservableCollection<BucketVM>();
        public bool CurrentUnreliable { get => _currentUnreliable; private set => Set(ref _currentUnreliable, value); }
        public string UnreliableText => L("Workforce_Unreliable");

        // ── structure ──
        public ObservableCollection<BucketVM> DepartmentBuckets { get; } = new ObservableCollection<BucketVM>();
        public ObservableCollection<BucketVM> PosteBuckets { get; } = new ObservableCollection<BucketVM>();

        public ICommand OpenBucketCommand { get; }
        public ICommand OpenHeadcountCommand { get; }
        public ICommand OpenEntriesCommand { get; }
        public ICommand OpenExitsCommand { get; }

        public void Load()
        {
            long companyId = _services.CompanyContext.ActiveId;
            if (companyId <= 0) return;

            DateTime today = DateTime.Today;
            DateTime start = PeriodStart(_period?.Key, today);
            _wa = _services.Dashboard.BuildWorkforce(companyId, start, today);

            HeadcountText = _wa.Headcount.ToString(Fr);
            AverageAgeText = _wa.AverageAge.HasValue ? string.Format(L("Workforce_AgeYears"), _wa.AverageAge.Value.ToString("0.#", Fr)) : "—";
            AverageAgeNote = _wa.AverageAge.HasValue ? string.Format(L("Workforce_AgeKnownNote"), _wa.AgeKnownCount, _wa.AgeTotalCount) : string.Empty;

            EntriesText = _wa.Entries.ToString(Fr);
            ExitsText = _wa.Exits.ToString(Fr);
            TurnoverText = _wa.TurnoverRate.ToString("0.#", Fr) + " %";
            TurnoverRaw = string.Format(L("Workforce_TurnoverRaw"), _wa.Exits, _wa.AverageHeadcount.ToString("0.#", Fr));
            TurnoverTooltip = L("Workforce_TurnoverTooltip");

            AsOfText = string.Format(L("Workforce_AsOf"), today.ToString("dd/MM/yyyy", Fr));
            PeriodLabel = string.Format(L("Workforce_PeriodLabel"), start.ToString("dd/MM/yyyy", Fr), today.ToString("dd/MM/yyyy", Fr));

            RebuildCurrent();
            Fill(DepartmentBuckets, _wa.ByDepartment, L("Workforce_ByDepartment"));
            Fill(PosteBuckets, _wa.ByPoste, L("Workforce_ByPoste"));
        }

        private void RebuildCurrent()
        {
            if (_wa == null || _axis == null) return;
            WorkforceDistribution d = _axis.Select(_wa);
            Fill(CurrentBuckets, d, _axis.Label);
            CurrentUnreliable = d.Unreliable;
        }

        private void Fill(ObservableCollection<BucketVM> target, WorkforceDistribution d, string axisLabel)
        {
            target.Clear();
            int max = d.Buckets.Count > 0 ? d.Buckets.Max(b => b.Count) : 0;
            foreach (WorkforceBucket b in d.Buckets)
            {
                string label = b.IsUnknown || !string.IsNullOrEmpty(b.LabelKey) ? L(b.LabelKey) : b.Label;
                double pct = d.Total > 0 ? (double)b.Count / d.Total : 0;
                target.Add(new BucketVM
                {
                    Label = label,
                    CountText = b.Count.ToString(Fr),
                    PctText = (pct * 100).ToString("0.#", Fr) + " %",
                    BarWidth = max > 0 ? (double)b.Count / max * BarMax : 0,
                    IsUnknown = b.IsUnknown,
                    EmployeeIds = b.EmployeeIds,
                    DrillTitle = axisLabel + " · " + label
                });
            }
        }

        private void OpenBucket(BucketVM b)
        {
            if (b != null) OpenList(b.DrillTitle, b.EmployeeIds);
        }

        private void OpenList(string title, IReadOnlyList<long> ids)
        {
            if (ids == null) return;
            var vm = new EmployeeListViewModel(_services, title, _services.CompanyContext.ActiveId, ids);
            var win = new Views.EmployeeListWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(win);
            vm.RequestClose = () => win.Close();
            win.ShowDialog();
        }

        private static DateTime PeriodStart(string key, DateTime today)
        {
            switch (key)
            {
                case "year": return new DateTime(today.Year, 1, 1);
                case "quarter":
                    int q = (today.Month - 1) / 3;
                    return new DateTime(today.Year, q * 3 + 1, 1);
                default: return new DateTime(today.Year, today.Month, 1);
            }
        }

        private string L(string key) => _services.Localization.GetString(key);
    }

    public sealed class PeriodOption
    {
        public PeriodOption(string key, string label) { Key = key; Label = label; }
        public string Key { get; }
        public string Label { get; }
    }

    public sealed class AxisOption
    {
        public AxisOption(string key, string label, Func<WorkforceAnalytics, WorkforceDistribution> select)
        { Key = key; Label = label; Select = select; }
        public string Key { get; }
        public string Label { get; }
        public Func<WorkforceAnalytics, WorkforceDistribution> Select { get; }
    }

    public sealed class BucketVM
    {
        public string Label { get; set; }
        public string CountText { get; set; }
        public string PctText { get; set; }
        public double BarWidth { get; set; }
        public bool IsUnknown { get; set; }
        public IReadOnlyList<long> EmployeeIds { get; set; }
        public string DrillTitle { get; set; }
    }
}
