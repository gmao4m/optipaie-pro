using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Core.Dtos;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The workforce (effectif) analytics panel. Purely presentational and DATA-FED: the parent
    /// dashboard builds the whole overview once (off the UI thread) and pushes the analytics here via
    /// <see cref="Apply"/> — this view-model never queries. It owns the axis switch (a client-side
    /// re-slice of the already-built distributions), the department donut and the drill-downs to the
    /// nominative list. All numbers come from the tested <see cref="Core.Interfaces.Services.IDashboardService"/>.
    /// </summary>
    public sealed class WorkforceViewModel : ObservableObject
    {
        private const double BarMax = 150.0;
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private WorkforceAnalytics _wa;
        private AxisOption _axis;

        private string _headcount = "0", _avgAge = "—", _avgAgeNote = string.Empty;
        private string _entries = "0", _exits = "0", _turnover = "0 %", _turnoverRaw = string.Empty, _turnoverTip = string.Empty;
        private bool _currentUnreliable;

        public WorkforceViewModel(AppServices services)
        {
            _services = services;

            Axes.Add(new AxisOption("contract", L("Workforce_Axis_Contract"), w => w.ByContract));
            Axes.Add(new AxisOption("category", L("Workforce_Axis_Category"), w => w.ByCategory));
            Axes.Add(new AxisOption("gender", L("Workforce_Axis_Gender"), w => w.ByGender));
            Axes.Add(new AxisOption("marital", L("Workforce_Axis_Marital"), w => w.ByMaritalStatus));
            Axes.Add(new AxisOption("age", L("Workforce_Axis_Age"), w => w.ByAgeBand));
            Axes.Add(new AxisOption("seniority", L("Workforce_Axis_Seniority"), w => w.BySeniority));
            _axis = Axes[0];

            OpenBucketCommand = new RelayCommand(p => OpenBucket(p as BucketVM));
            OpenHeadcountCommand = new RelayCommand(() => OpenList(L("Workforce_Headcount"), _wa?.HeadcountIds));
            OpenEntriesCommand = new RelayCommand(() => OpenList(L("Workforce_Entries"), _wa?.EntryIds));
            OpenExitsCommand = new RelayCommand(() => OpenList(L("Workforce_Exits"), _wa?.ExitIds));
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

        // ── department donut ──
        public ObservableCollection<DeptSlice> DeptSlices { get; } = new ObservableCollection<DeptSlice>();
        public bool HasDepartments => DeptSlices.Count > 0;

        public ICommand OpenBucketCommand { get; }
        public ICommand OpenHeadcountCommand { get; }
        public ICommand OpenEntriesCommand { get; }
        public ICommand OpenExitsCommand { get; }

        /// <summary>Pushes freshly-built analytics into the panel (called on the UI thread by the parent).</summary>
        public void Apply(WorkforceAnalytics wa)
        {
            _wa = wa;
            if (wa == null) return;

            HeadcountText = wa.Headcount.ToString(Fr);
            AverageAgeText = wa.AverageAge.HasValue ? string.Format(L("Workforce_AgeYears"), wa.AverageAge.Value.ToString("0.#", Fr)) : "—";
            AverageAgeNote = wa.AverageAge.HasValue ? string.Format(L("Workforce_AgeKnownNote"), wa.AgeKnownCount, wa.AgeTotalCount) : string.Empty;
            EntriesText = wa.Entries.ToString(Fr);
            ExitsText = wa.Exits.ToString(Fr);
            TurnoverText = wa.TurnoverRate.ToString("0.#", Fr) + " %";
            TurnoverRaw = string.Format(L("Workforce_TurnoverRaw"), wa.Exits, wa.AverageHeadcount.ToString("0.#", Fr));
            TurnoverTooltip = L("Workforce_TurnoverTooltip");

            RebuildCurrent();
            BuildDonut(wa.ByDepartment);
        }

        private void RebuildCurrent()
        {
            if (_wa == null || _axis == null) return;
            WorkforceDistribution d = _axis.Select(_wa);
            CurrentBuckets.Clear();
            int max = d.Buckets.Count > 0 ? d.Buckets.Max(b => b.Count) : 0;
            foreach (WorkforceBucket b in d.Buckets)
            {
                string label = b.IsUnknown || !string.IsNullOrEmpty(b.LabelKey) ? L(b.LabelKey) : b.Label;
                double pct = d.Total > 0 ? (double)b.Count / d.Total : 0;
                CurrentBuckets.Add(new BucketVM
                {
                    Label = label,
                    CountText = b.Count.ToString(Fr),
                    PctText = (pct * 100).ToString("0.#", Fr) + " %",
                    BarWidth = max > 0 ? (double)b.Count / max * BarMax : 0,
                    IsUnknown = b.IsUnknown,
                    EmployeeIds = b.EmployeeIds,
                    DrillTitle = _axis.Label + " · " + label
                });
            }
            CurrentUnreliable = d.Unreliable;
        }

        private void BuildDonut(WorkforceDistribution dept)
        {
            DeptSlices.Clear();
            if (dept == null) { Raise(nameof(HasDepartments)); return; }

            int total = dept.Total;
            // Maximally-distinct hues first (green · gold · blue · red · violet-blue · deep green)
            // so the first few departments never read as the same colour.
            Brush[] palette =
            {
                Res("Brand", Color.FromRgb(0x0E, 0x9F, 0x6E)),
                Res("Accent", Color.FromRgb(0xE3, 0xB3, 0x41)),
                Res("Employer", Color.FromRgb(0x6C, 0x87, 0xEC)),
                Res("Deduction", Color.FromRgb(0xE5, 0x48, 0x4D)),
                Res("StatusPending", Color.FromRgb(0xC0, 0x8A, 0x2E)),
                Res("BrandPressed", Color.FromRgb(0x05, 0x7A, 0x55))
            };

            double angle = -90.0;
            int ci = 0;
            foreach (WorkforceBucket b in dept.Buckets)
            {
                string label = b.IsUnknown ? L("Workforce_Unknown") : b.Label;
                double frac = total > 0 ? (double)b.Count / total : 0.0;
                double sweep = frac * 360.0;
                if (sweep >= 359.9) sweep = 359.9;
                DeptSlices.Add(new DeptSlice
                {
                    Name = label,
                    CountText = b.Count.ToString(Fr),
                    PercentText = Math.Round(frac * 100).ToString("0", Fr) + " %",
                    Fill = palette[ci % palette.Length],
                    PathData = DonutArc(96, 96, 84, 54, angle, angle + sweep),
                    EmployeeIds = b.EmployeeIds,
                    DrillTitle = L("Workforce_ByDepartment") + " · " + label
                });
                angle += sweep;
                ci++;
            }
            Raise(nameof(HasDepartments));
        }

        public ICommand OpenDeptCommand => new RelayCommand(p => { if (p is DeptSlice s) OpenList(s.DrillTitle, s.EmployeeIds); });

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

        private string L(string key) => _services.Localization.GetString(key);

        /// <summary>Resolves a themed brush by resource key, with a fixed fallback headlessly.</summary>
        private static Brush Res(string key, Color fallback)
        {
            Brush b = Application.Current != null ? Application.Current.TryFindResource(key) as Brush : null;
            if (b != null) return b;
            var solid = new SolidColorBrush(fallback);
            solid.Freeze();
            return solid;
        }

        /// <summary>Path for one donut ring segment (degrees, clockwise from 12 o'clock).</summary>
        private static string DonutArc(double cx, double cy, double outerR, double innerR, double a0, double a1)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            double r0 = a0 * Math.PI / 180.0, r1 = a1 * Math.PI / 180.0;
            double ox0 = cx + outerR * Math.Cos(r0), oy0 = cy + outerR * Math.Sin(r0);
            double ox1 = cx + outerR * Math.Cos(r1), oy1 = cy + outerR * Math.Sin(r1);
            double ix1 = cx + innerR * Math.Cos(r1), iy1 = cy + innerR * Math.Sin(r1);
            double ix0 = cx + innerR * Math.Cos(r0), iy0 = cy + innerR * Math.Sin(r0);
            int large = (a1 - a0) > 180.0 ? 1 : 0;
            return string.Format(ci,
                "M {0:0.##},{1:0.##} A {2:0.##},{2:0.##} 0 {3} 1 {4:0.##},{5:0.##} L {6:0.##},{7:0.##} A {8:0.##},{8:0.##} 0 {3} 0 {9:0.##},{10:0.##} Z",
                ox0, oy0, outerR, large, ox1, oy1, ix1, iy1, innerR, ix0, iy0);
        }
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

    /// <summary>One department segment of the effectif donut (+ its legend row).</summary>
    public sealed class DeptSlice
    {
        public string Name { get; set; }
        public string CountText { get; set; }
        public string PercentText { get; set; }
        public string PathData { get; set; }
        public Brush Fill { get; set; }
        public IReadOnlyList<long> EmployeeIds { get; set; }
        public string DrillTitle { get; set; }
    }
}
