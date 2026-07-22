using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;

namespace OptiPaie.Desktop.ViewModels.Performance
{
    /// <summary>Shared helpers for the Performance hub tabs (band colours, formatting).</summary>
    internal static class PerfUi
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public static readonly Brush Green = Freeze("#2E9E6C");
        public static readonly Brush Teal = Freeze("#0F9B8E");
        public static readonly Brush Amber = Freeze("#D9A441");
        public static readonly Brush Orange = Freeze("#E08A3C");
        public static readonly Brush Red = Freeze("#C24444");
        public static readonly Brush Muted = Freeze("#8B8F99");
        public static readonly Brush Navy = Freeze("#1B2A4A");

        public static Brush BandBrush(decimal percent)
        {
            if (percent >= 80m) return Green;
            if (percent >= 70m) return Teal;
            if (percent >= 60m) return Amber;
            if (percent >= 50m) return Orange;
            return Red;
        }

        public static string Pct(decimal v) => v.ToString("0.#", Fr) + " %";
        public static string Num(decimal v) => v.ToString("0.##", Fr);

        private static Brush Freeze(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }
    }

    // ===================================================================== Cycles

    public sealed class CycleCardViewModel
    {
        public CycleCardViewModel(CycleSummary s) { S = s; }
        public CycleSummary S { get; }
        public long CycleId => S.CycleId;
        public string Name => S.Name;
        public string TypeLabel => S.CycleTypeLabel;
        public string StatusLabel => S.StatusLabel;
        public string Period => S.StartDate.ToString("dd/MM/yyyy") + " → " + S.EndDate.ToString("dd/MM/yyyy");
        public string CompletionText => S.CompletedReviews + " / " + S.TotalReviews + " · " + PerfUi.Pct(S.CompletionPercent);
        public double CompletionValue => (double)S.CompletionPercent;
        public Brush StatusBrush => S.Status == PerformanceCycleStatus.Completed ? PerfUi.Green
            : S.Status == PerformanceCycleStatus.Active ? PerfUi.Teal
            : S.Status == PerformanceCycleStatus.Cancelled ? PerfUi.Red : PerfUi.Muted;
    }

    public sealed class CycleReviewRowViewModel
    {
        public CycleReviewRowViewModel(CycleReviewRow r) { R = r; }
        public CycleReviewRow R { get; }
        public long ReviewId => R.ReviewId;
        public string EmployeeName => R.EmployeeName;
        public string Department => R.Department;
        public string Reviewer => R.Reviewer;
        public string StatusLabel => R.StatusLabel;
        public string ScoreText => R.Status == PerformanceStatus.Completed
            ? PerfUi.Num(R.OverallScore) + " / " + PerfUi.Num(R.ScaleMax) + "  (" + R.Rating + ")"
            : "—";
        public string DueText => R.DueDate.HasValue ? R.DueDate.Value.ToString("dd/MM/yyyy") : "—";
        public Brush StatusBrush => R.Status == PerformanceStatus.Completed ? PerfUi.Green : (R.IsOverdue ? PerfUi.Red : PerfUi.Amber);
    }

    public sealed class DeptCompletionRowViewModel
    {
        public DeptCompletionRowViewModel(DeptCompletionRow r) { R = r; }
        public DeptCompletionRow R { get; }
        public string Department => R.Department;
        public string CompletionText => R.Completed + " / " + R.Total + " · " + PerfUi.Pct(R.CompletionPercent);
        public double CompletionValue => (double)R.CompletionPercent;
    }

    public sealed class CyclesTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Func<long> _companyId;
        private CycleCardViewModel _selectedCycle;
        private CycleReviewRowViewModel _selectedReview;
        private string _status = string.Empty;

        public CyclesTabViewModel(AppServices services, Func<long> companyId)
        {
            _services = services;
            _companyId = companyId;
            LaunchCommand = new RelayCommand(Launch);
            OpenReviewCommand = new RelayCommand(OpenReview, () => _selectedReview != null);
            DeleteCommand = new RelayCommand(DeleteCycle, () => _selectedCycle != null);
            RefreshCommand = new RelayCommand(Refresh);
        }

        public ObservableCollection<CycleCardViewModel> Cycles { get; } = new ObservableCollection<CycleCardViewModel>();
        public ObservableCollection<CycleReviewRowViewModel> Reviews { get; } = new ObservableCollection<CycleReviewRowViewModel>();
        public ObservableCollection<DeptCompletionRowViewModel> Departments { get; } = new ObservableCollection<DeptCompletionRowViewModel>();

        public CycleCardViewModel SelectedCycle
        {
            get => _selectedCycle;
            set { if (Set(ref _selectedCycle, value)) LoadDetail(); }
        }

        public CycleReviewRowViewModel SelectedReview
        {
            get => _selectedReview;
            set => Set(ref _selectedReview, value);
        }

        public bool HasCycles => Cycles.Count > 0;
        public string StatusMessage { get => _status; private set => Set(ref _status, value); }

        public ICommand LaunchCommand { get; }
        public ICommand OpenReviewCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RefreshCommand { get; }

        public void Refresh()
        {
            Cycles.Clear();
            long companyId = _companyId();
            if (companyId > 0)
            {
                foreach (CycleSummary c in _services.Performance.GetCycles(companyId))
                {
                    Cycles.Add(new CycleCardViewModel(c));
                }
            }
            Raise(nameof(HasCycles));
            SelectedCycle = Cycles.FirstOrDefault();
            StatusMessage = Cycles.Count + " campagne(s)";
        }

        private void LoadDetail()
        {
            Reviews.Clear();
            Departments.Clear();
            if (_selectedCycle == null) return;

            CycleDetail detail = _services.Performance.GetCycleDetail(_selectedCycle.CycleId);
            if (detail == null) return;

            foreach (CycleReviewRow r in detail.Reviews) Reviews.Add(new CycleReviewRowViewModel(r));
            foreach (DeptCompletionRow d in detail.ByDepartment) Departments.Add(new DeptCompletionRowViewModel(d));
            SelectedReview = Reviews.FirstOrDefault();
        }

        private void Launch()
        {
            long companyId = _companyId();
            if (companyId <= 0) { Dialogs.Info("Sélectionnez d'abord une entreprise."); return; }

            var vm = new CycleLaunchViewModel(_services, companyId);
            var window = new CycleLaunchWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            if (window.ShowDialog() == true)
            {
                Refresh();
                StatusMessage = "Campagne lancée.";
            }
        }

        private void OpenReview()
        {
            var vm = new PerformanceReviewFormViewModel(_services, _selectedReview.ReviewId);
            var window = new PerformanceReviewWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            if (window.ShowDialog() == true)
            {
                LoadDetail();
                Refresh();
            }
        }

        private void DeleteCycle()
        {
            if (!Dialogs.Confirm("Supprimer cette campagne et ses évaluations ?")) return;
            Result r = _services.Performance.DeleteCycle(_selectedCycle.CycleId);
            if (r.IsFailure) { Dialogs.Error(r.Error); return; }
            Refresh();
        }
    }

    // ================================================================== Templates

    public sealed class TemplateCardViewModel
    {
        public TemplateCardViewModel(TemplateSummary s) { S = s; }
        public TemplateSummary S { get; }
        public long TemplateId => S.TemplateId;
        public string Name => S.Name;
        public string KindLabel => S.KindLabel;
        public string DepartmentTag => string.IsNullOrWhiteSpace(S.DepartmentTag) ? "Tous départements" : S.DepartmentTag;
        public string CriteriaText => S.CriteriaCount + " critère(s) · /" + PerfUi.Num(S.ScaleMax);
        public bool IsBuiltIn => S.IsBuiltIn;
        public string OriginText => S.IsBuiltIn ? "Prédéfini" : "Personnalisé";
        public Brush OriginBrush => S.IsBuiltIn ? PerfUi.Muted : PerfUi.Teal;
    }

    public sealed class TemplatesTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Func<long> _companyId;
        private TemplateCardViewModel _selected;
        private string _status = string.Empty;

        public TemplatesTabViewModel(AppServices services, Func<long> companyId)
        {
            _services = services;
            _companyId = companyId;
            DuplicateCommand = new RelayCommand(Duplicate, () => _selected != null);
            EditCommand = new RelayCommand(Edit, () => _selected != null && !_selected.IsBuiltIn);
            DeleteCommand = new RelayCommand(Delete, () => _selected != null && !_selected.IsBuiltIn);
            RefreshCommand = new RelayCommand(Refresh);
        }

        public ObservableCollection<TemplateCardViewModel> Templates { get; } = new ObservableCollection<TemplateCardViewModel>();

        public TemplateCardViewModel Selected { get => _selected; set => Set(ref _selected, value); }
        public bool HasTemplates => Templates.Count > 0;
        public string StatusMessage { get => _status; private set => Set(ref _status, value); }

        public ICommand DuplicateCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RefreshCommand { get; }

        public void Refresh()
        {
            Templates.Clear();
            long companyId = _companyId();
            if (companyId > 0)
            {
                foreach (TemplateSummary t in _services.Performance.GetTemplates(companyId))
                {
                    Templates.Add(new TemplateCardViewModel(t));
                }
            }
            Raise(nameof(HasTemplates));
            StatusMessage = Templates.Count + " modèle(s)";
        }

        private void Duplicate()
        {
            long companyId = _companyId();
            var vm = new TemplateEditorViewModel(_services, companyId, _selected.TemplateId, duplicate: true);
            if (ShowEditor(vm)) Refresh();
        }

        private void Edit()
        {
            long companyId = _companyId();
            var vm = new TemplateEditorViewModel(_services, companyId, _selected.TemplateId, duplicate: false);
            if (ShowEditor(vm)) Refresh();
        }

        private bool ShowEditor(TemplateEditorViewModel vm)
        {
            var window = new TemplateEditorWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer ce modèle personnalisé ?")) return;
            Result r = _services.Performance.DeleteTemplate(_selected.TemplateId);
            if (r.IsFailure) { Dialogs.Error(r.Error); return; }
            Refresh();
        }
    }

    // ================================================================== Dashboard

    public sealed class PerformerRowViewModel
    {
        public PerformerRowViewModel(PerformerRow r, int rank) { R = r; Rank = rank; }
        public PerformerRow R { get; }
        public int Rank { get; }
        public string EmployeeName => R.EmployeeName;
        public string Department => R.Department;
        public string ScoreText => PerfUi.Num(R.LatestScore) + " / " + PerfUi.Num(R.ScaleMax);
        public string PercentText => PerfUi.Pct(R.ScorePercent);
        public string Rating => R.Rating;
        public Brush Brush => PerfUi.BandBrush(R.ScorePercent);
    }

    public sealed class DeptScoreRowViewModel
    {
        public DeptScoreRowViewModel(DeptScoreRow r) { R = r; }
        public DeptScoreRow R { get; }
        public string Department => R.Department;
        public string AverageText => PerfUi.Pct(R.AveragePercent);
        public string CountText => R.ReviewCount + " éval.";
        public double BarValue => (double)R.AveragePercent;
        public Brush Brush => PerfUi.BandBrush(R.AveragePercent);
    }

    public sealed class TrendRowViewModel
    {
        public TrendRowViewModel(TrendPoint t) { T = t; }
        public TrendPoint T { get; }
        public string Label => T.Label;
        public string AverageText => PerfUi.Pct(T.AveragePercent);
        public string CountText => T.ReviewCount + " éval.";
        public double BarValue => (double)T.AveragePercent;
    }

    public sealed class DashboardTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Func<long> _companyId;
        private string _headline = string.Empty;
        private bool _hasData;

        public DashboardTabViewModel(AppServices services, Func<long> companyId)
        {
            _services = services;
            _companyId = companyId;
        }

        public ObservableCollection<PerformerRowViewModel> Top { get; } = new ObservableCollection<PerformerRowViewModel>();
        public ObservableCollection<PerformerRowViewModel> Bottom { get; } = new ObservableCollection<PerformerRowViewModel>();
        public ObservableCollection<DeptScoreRowViewModel> Departments { get; } = new ObservableCollection<DeptScoreRowViewModel>();
        public ObservableCollection<TrendRowViewModel> Trend { get; } = new ObservableCollection<TrendRowViewModel>();

        public string Headline { get => _headline; private set => Set(ref _headline, value); }
        public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
        public bool IsEmpty => !_hasData;

        public void Refresh()
        {
            Top.Clear(); Bottom.Clear(); Departments.Clear(); Trend.Clear();
            long companyId = _companyId();
            PerformanceDashboard d = companyId > 0 ? _services.Performance.GetDashboard(companyId) : null;

            if (d == null || d.ReviewCount == 0)
            {
                HasData = false;
                Raise(nameof(IsEmpty));
                Headline = "Aucune évaluation finalisée pour le moment.";
                return;
            }

            int rank = 1;
            foreach (PerformerRow r in d.TopPerformers) Top.Add(new PerformerRowViewModel(r, rank++));
            rank = 1;
            foreach (PerformerRow r in d.BottomPerformers) Bottom.Add(new PerformerRowViewModel(r, rank++));
            foreach (DeptScoreRow r in d.DepartmentAverages) Departments.Add(new DeptScoreRowViewModel(r));
            foreach (TrendPoint t in d.Trend) Trend.Add(new TrendRowViewModel(t));

            HasData = true;
            Raise(nameof(IsEmpty));
            Headline = "Moyenne entreprise : " + PerfUi.Pct(d.CompanyAveragePercent) + "  ·  " + d.ReviewCount + " évaluation(s)";
        }
    }

    // ================================================================ Calibration

    public sealed class CalibrationRowViewModel
    {
        public CalibrationRowViewModel(CalibrationDeptRow r) { R = r; }
        public CalibrationDeptRow R { get; }
        public string Department => R.Department;
        public string CountText => R.ReviewCount.ToString();
        public string AverageText => PerfUi.Pct(R.AveragePercent);
        public string DeltaText => (R.DeltaVsCompany >= 0m ? "+" : "") + PerfUi.Num(R.DeltaVsCompany) + " pts";
        public double BarValue => (double)R.AveragePercent;
        public Brush Brush => PerfUi.BandBrush(R.AveragePercent);
        public bool IsOutlier => R.IsOutlierHigh || R.IsOutlierLow;
        public string OutlierText => R.IsOutlierHigh ? "Indulgent" : (R.IsOutlierLow ? "Sévère" : "");
        public Brush OutlierBrush => R.IsOutlierHigh ? PerfUi.Amber : PerfUi.Red;
        // Distribution counts: Insuffisant..Excellent
        public int D0 => R.Distribution.Length > 0 ? R.Distribution[0] : 0;
        public int D1 => R.Distribution.Length > 1 ? R.Distribution[1] : 0;
        public int D2 => R.Distribution.Length > 2 ? R.Distribution[2] : 0;
        public int D3 => R.Distribution.Length > 3 ? R.Distribution[3] : 0;
        public int D4 => R.Distribution.Length > 4 ? R.Distribution[4] : 0;
    }

    public sealed class CalibrationTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Func<long> _companyId;
        private readonly Func<int> _year;
        private string _headline = string.Empty;
        private bool _hasData;

        public CalibrationTabViewModel(AppServices services, Func<long> companyId, Func<int> year)
        {
            _services = services;
            _companyId = companyId;
            _year = year;
        }

        public ObservableCollection<CalibrationRowViewModel> Departments { get; } = new ObservableCollection<CalibrationRowViewModel>();
        public string Headline { get => _headline; private set => Set(ref _headline, value); }
        public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
        public bool IsEmpty => !_hasData;

        public void Refresh()
        {
            Departments.Clear();
            long companyId = _companyId();
            CalibrationView v = companyId > 0 ? _services.Performance.GetCalibration(companyId, _year()) : null;

            if (v == null || v.ReviewCount == 0)
            {
                HasData = false;
                Raise(nameof(IsEmpty));
                Headline = "Aucune évaluation finalisée pour " + _year() + ".";
                return;
            }

            foreach (CalibrationDeptRow r in v.Departments) Departments.Add(new CalibrationRowViewModel(r));
            HasData = true;
            Raise(nameof(IsEmpty));
            int outliers = v.Departments.Count(d => d.IsOutlierHigh || d.IsOutlierLow);
            Headline = "Moyenne entreprise : " + PerfUi.Pct(v.CompanyAveragePercent) +
                       (outliers > 0 ? "  ·  " + outliers + " département(s) atypique(s)" : "  ·  répartition homogène");
        }
    }

    // ====================================================================== Goals

    public sealed class GoalRowViewModel
    {
        public GoalRowViewModel(GoalRow r) { R = r; }
        public GoalRow R { get; }
        public long GoalId => R.GoalId;
        public long EmployeeId => R.EmployeeId;
        public string EmployeeName => R.EmployeeName;
        public string Title => R.Title;
        public string TargetMetric => R.TargetMetric;
        public string DueText => R.DueDate.HasValue ? R.DueDate.Value.ToString("dd/MM/yyyy") : "—";
        public string ProgressText => PerfUi.Num(R.ProgressPercent) + " %";
        public double ProgressValue => (double)R.ProgressPercent;
        public string StatusLabel => R.StatusLabel;
        public bool IsActive => R.Status == PerformanceGoalStatus.Active;
        public Brush StatusBrush => R.Status == PerformanceGoalStatus.Achieved ? PerfUi.Green
            : R.Status == PerformanceGoalStatus.Missed ? PerfUi.Red
            : R.Status == PerformanceGoalStatus.Cancelled ? PerfUi.Muted
            : (R.IsOverdue ? PerfUi.Amber : PerfUi.Teal);
    }

    public sealed class GoalsTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Func<long> _companyId;
        private GoalRowViewModel _selected;
        private string _status = string.Empty;

        public GoalsTabViewModel(AppServices services, Func<long> companyId)
        {
            _services = services;
            _companyId = companyId;
            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selected != null);
            AchieveCommand = new RelayCommand(Achieve, () => _selected != null && _selected.IsActive);
            DeleteCommand = new RelayCommand(Delete, () => _selected != null);
            RefreshCommand = new RelayCommand(Refresh);
        }

        public ObservableCollection<GoalRowViewModel> Goals { get; } = new ObservableCollection<GoalRowViewModel>();
        public GoalRowViewModel Selected { get => _selected; set => Set(ref _selected, value); }
        public bool HasGoals => Goals.Count > 0;
        public string StatusMessage { get => _status; private set => Set(ref _status, value); }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand AchieveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RefreshCommand { get; }

        public void Refresh()
        {
            Goals.Clear();
            long companyId = _companyId();
            if (companyId > 0)
            {
                foreach (GoalRow g in _services.Performance.GetCompanyGoals(companyId)) Goals.Add(new GoalRowViewModel(g));
            }
            Raise(nameof(HasGoals));
            int active = Goals.Count(g => g.IsActive);
            StatusMessage = Goals.Count + " objectif(s) · " + active + " actif(s)";
        }

        private void New()
        {
            long companyId = _companyId();
            if (companyId <= 0) { Dialogs.Info("Sélectionnez d'abord une entreprise."); return; }
            var vm = new GoalEditViewModel(_services, companyId, null);
            if (Show(vm)) Refresh();
        }

        private void Edit()
        {
            long companyId = _companyId();
            var vm = new GoalEditViewModel(_services, companyId, _selected.R);
            if (Show(vm)) Refresh();
        }

        private bool Show(GoalEditViewModel vm)
        {
            var window = new GoalEditWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }

        private void Achieve()
        {
            Result r = _services.Performance.SetGoalStatus(_selected.GoalId, PerformanceGoalStatus.Achieved);
            if (r.IsFailure) { Dialogs.Error(r.Error); return; }
            Refresh();
        }

        private void Delete()
        {
            if (!Dialogs.Confirm("Supprimer cet objectif ?")) return;
            _services.Performance.DeleteGoal(_selected.GoalId);
            Refresh();
        }
    }

    // ================================================================= Comparison

    public sealed class EmployeePickViewModel : ObservableObject
    {
        private bool _isChecked;
        public EmployeePickViewModel(Employee e) { Id = e.Id; Name = (e.LastNameFr + " " + e.FirstNameFr).Trim(); Department = e.Department; }
        public long Id { get; }
        public string Name { get; }
        public string Department { get; }
        public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
    }

    public sealed class ComparisonColumnViewModel
    {
        public ComparisonColumnViewModel(ComparisonColumn c) { C = c; }
        public ComparisonColumn C { get; }
        public string EmployeeName => C.EmployeeName;
        public string Department => C.Department;
        public string Poste => C.Poste;
        public string LatestText => C.HasReviews ? PerfUi.Num(C.LatestScore) + " / " + PerfUi.Num(C.ScaleMax) : "—";
        public string PercentText => C.HasReviews ? PerfUi.Pct(C.LatestPercent) : "—";
        public string Rating => C.Rating;
        public string ReviewCountText => C.ReviewCount.ToString();
        public string AverageText => C.HasReviews ? PerfUi.Pct(C.AveragePercent) : "—";
        public string GoalsText => C.ActiveGoals + " actif(s) · " + PerfUi.Pct(C.GoalCompletionPercent);
        public Brush Brush => C.HasReviews ? PerfUi.BandBrush(C.LatestPercent) : PerfUi.Muted;
    }

    public sealed class ComparisonTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Func<long> _companyId;
        private string _status = string.Empty;

        public ComparisonTabViewModel(AppServices services, Func<long> companyId)
        {
            _services = services;
            _companyId = companyId;
            CompareCommand = new RelayCommand(Compare);
            ClearCommand = new RelayCommand(() => { foreach (var e in Employees) e.IsChecked = false; Columns.Clear(); });
        }

        public ObservableCollection<EmployeePickViewModel> Employees { get; } = new ObservableCollection<EmployeePickViewModel>();
        public ObservableCollection<ComparisonColumnViewModel> Columns { get; } = new ObservableCollection<ComparisonColumnViewModel>();
        public string StatusMessage { get => _status; private set => Set(ref _status, value); }

        public ICommand CompareCommand { get; }
        public ICommand ClearCommand { get; }

        public void Refresh()
        {
            Employees.Clear();
            Columns.Clear();
            long companyId = _companyId();
            if (companyId > 0)
            {
                foreach (Employee e in _services.Employees.GetByCompany(companyId, false).OrderBy(e => e.LastNameFr))
                {
                    Employees.Add(new EmployeePickViewModel(e));
                }
            }
            StatusMessage = "Cochez 2 employés ou plus, puis comparez.";
        }

        private void Compare()
        {
            List<long> ids = Employees.Where(e => e.IsChecked).Select(e => e.Id).ToList();
            if (ids.Count < 2) { Dialogs.Info("Sélectionnez au moins 2 employés à comparer."); return; }

            Columns.Clear();
            EmployeeComparison cmp = _services.Performance.Compare(ids);
            foreach (ComparisonColumn c in cmp.Employees) Columns.Add(new ComparisonColumnViewModel(c));
            StatusMessage = Columns.Count + " employé(s) comparé(s).";
        }
    }

    // ===================================================================== Career

    public sealed class TimelineItemViewModel
    {
        public TimelineItemViewModel(CareerTimelineItem i) { I = i; }
        public CareerTimelineItem I { get; }
        public string DateText => I.Date.ToString("dd/MM/yyyy");
        public string Title => I.Title;
        public string Detail => I.Detail;
        public string ValueText => I.ValueText;
        public string KindLabel =>
            I.Kind == "review" ? "Évaluation" :
            I.Kind == "goal" ? "Objectif" :
            I.Kind == "promotion" ? "Promotion" : "Récompense";
        public Brush KindBrush =>
            I.Kind == "review" ? PerfUi.Navy :
            I.Kind == "goal" ? PerfUi.Teal :
            I.Kind == "promotion" ? PerfUi.Green : PerfUi.Amber;
    }

    public sealed class CareerTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly Func<long> _companyId;
        private EmployeePickViewModel _selectedEmployee;
        private string _employeeName = string.Empty;

        public CareerTabViewModel(AppServices services, Func<long> companyId)
        {
            _services = services;
            _companyId = companyId;
            PromotionCommand = new RelayCommand(LogPromotion, () => _selectedEmployee != null);
            RewardCommand = new RelayCommand(LogReward, () => _selectedEmployee != null);
        }

        public ObservableCollection<EmployeePickViewModel> Employees { get; } = new ObservableCollection<EmployeePickViewModel>();
        public ObservableCollection<TimelineItemViewModel> Items { get; } = new ObservableCollection<TimelineItemViewModel>();

        public EmployeePickViewModel SelectedEmployee
        {
            get => _selectedEmployee;
            set { if (Set(ref _selectedEmployee, value)) LoadTimeline(); }
        }

        public string EmployeeName { get => _employeeName; private set => Set(ref _employeeName, value); }
        public bool HasItems => Items.Count > 0;

        public ICommand PromotionCommand { get; }
        public ICommand RewardCommand { get; }

        public void Refresh()
        {
            long companyId = _companyId();
            long keep = _selectedEmployee?.Id ?? 0;
            Employees.Clear();
            if (companyId > 0)
            {
                foreach (Employee e in _services.Employees.GetByCompany(companyId, false).OrderBy(e => e.LastNameFr))
                {
                    Employees.Add(new EmployeePickViewModel(e));
                }
            }
            SelectedEmployee = Employees.FirstOrDefault(e => e.Id == keep) ?? Employees.FirstOrDefault();
        }

        private void LoadTimeline()
        {
            Items.Clear();
            if (_selectedEmployee == null) { EmployeeName = string.Empty; Raise(nameof(HasItems)); return; }

            CareerTimeline t = _services.Performance.GetCareerTimeline(_selectedEmployee.Id);
            EmployeeName = t.EmployeeName;
            foreach (CareerTimelineItem i in t.Items) Items.Add(new TimelineItemViewModel(i));
            Raise(nameof(HasItems));
        }

        private void LogPromotion()
        {
            var vm = new CareerEventViewModel(_services, _selectedEmployee.Id, _selectedEmployee.Name, isPromotion: true);
            if (Show(vm)) LoadTimeline();
        }

        private void LogReward()
        {
            var vm = new CareerEventViewModel(_services, _selectedEmployee.Id, _selectedEmployee.Name, isPromotion: false);
            if (Show(vm)) LoadTimeline();
        }

        private bool Show(CareerEventViewModel vm)
        {
            var window = new CareerEventWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            App.ApplyFlowDirection(window);
            vm.RequestClose = ok => window.DialogResult = ok;
            return window.ShowDialog() == true;
        }
    }
}
