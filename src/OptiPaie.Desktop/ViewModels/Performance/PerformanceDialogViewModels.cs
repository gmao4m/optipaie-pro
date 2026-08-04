using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels.Performance
{
    /// <summary>A dialog VM the shared show-helper can close with a result.</summary>
    public interface IHasDialogResult { Action<bool> RequestClose { set; } }

    internal static class PerfDialogs
    {
        public static bool Show(Window window, IHasDialogResult vm)
        {
            window.Owner = Application.Current == null ? null : Application.Current.MainWindow;
            OptiPaie.Desktop.App.ApplyFlowDirection(window);
            vm.RequestClose = ok => { try { window.DialogResult = ok; } catch { window.Close(); } };
            return window.ShowDialog() == true;
        }

        public static string Err(Result r) =>
            string.IsNullOrEmpty(r.ErrorCode) ? r.Error : TranslationSource.Instance[r.ErrorCode];
    }

    // Enum option carriers for the combo boxes (localised label + value).
    public sealed class CadenceOption { public PeriodCadence Value { get; set; } public string Label => PerfLabels.CadenceLabel(Value); }
    public sealed class CategoryOption { public CriterionCategory Value { get; set; } public string Label => PerfLabels.CategoryLabel(Value); }
    public sealed class ScoreTypeOption { public ScoreType Value { get; set; } public string Label => PerfLabels.ScoreTypeLabel(Value); }

    // ======================================================================
    //  PERIOD editor
    // ======================================================================
    public sealed class PeriodEditViewModel : ObservableObject, IHasDialogResult
    {
        private readonly AppServices _services;
        private readonly long _companyId;
        private readonly long _periodId;
        private string _name;
        private PeriodCadence _cadence = PeriodCadence.Monthly;
        private DateTime _start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime _end = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month));

        public PeriodEditViewModel(AppServices services, long companyId, long periodId = 0)
        {
            _services = services; _companyId = companyId; _periodId = periodId;
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            _name = "Campagne " + DateTime.Today.Year;
            if (periodId > 0)
            {
                EvalPeriod p = _services.Performance.GetPeriod(periodId);
                if (p != null) { _name = p.Name; _cadence = p.Cadence; _start = p.StartDate; _end = p.EndDate; }
            }
        }

        public IReadOnlyList<CadenceOption> Cadences { get; } = new[]
        {
            new CadenceOption { Value = PeriodCadence.Weekly },
            new CadenceOption { Value = PeriodCadence.Monthly },
            new CadenceOption { Value = PeriodCadence.Yearly }
        };

        public string Name { get => _name; set => Set(ref _name, value); }
        public PeriodCadence Cadence { get => _cadence; set => Set(ref _cadence, value); }
        public DateTime StartDate { get => _start; set => Set(ref _start, value); }
        public DateTime EndDate { get => _end; set => Set(ref _end, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public Action<bool> RequestClose { private get; set; }

        private void Save()
        {
            var period = new EvalPeriod
            {
                Id = _periodId, CompanyId = _companyId, Name = _name,
                Cadence = _cadence, StartDate = _start, EndDate = _end,
                Status = PeriodStatus.Open
            };
            Result<long> r = _services.Performance.SavePeriod(period);
            if (r.IsFailure) { Dialogs.Error(PerfDialogs.Err(r)); return; }
            RequestClose?.Invoke(true);
        }
    }

    // ======================================================================
    //  EVALUATION form (the scoring screen)
    // ======================================================================
    public sealed class EvaluationFormViewModel : ObservableObject, IHasDialogResult
    {
        private readonly AppServices _services;
        private readonly long _evaluationId;
        private Evaluation _evaluation;
        private List<EvaluationScore> _scores = new List<EvaluationScore>();
        private long _companyId, _employeeId;
        private DateTime _periodStart, _periodEnd;
        private string _employeeName = "—", _employeeMeta = string.Empty, _periodName = string.Empty;
        private string _totalText = "0 / 100", _bandLabel = string.Empty, _bandKind = "neutral", _note;
        private int _positive, _negative;

        public EvaluationFormViewModel(AppServices services, long evaluationId)
        {
            _services = services; _evaluationId = evaluationId;
            SaveCommand = new RelayCommand(() => Save(false));
            CompleteCommand = new RelayCommand(() => Save(true));
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            AddPositiveCommand = new RelayCommand(() => AddBehavior(true));
            AddNegativeCommand = new RelayCommand(() => AddBehavior(false));
            Load();
        }

        public ObservableCollection<CriterionCardViewModel> Criteria { get; } = new ObservableCollection<CriterionCardViewModel>();
        public ObservableCollection<BehaviorItem> Behaviors { get; } = new ObservableCollection<BehaviorItem>();

        public string EmployeeName { get => _employeeName; private set => Set(ref _employeeName, value); }
        public string EmployeeMeta { get => _employeeMeta; private set => Set(ref _employeeMeta, value); }
        public string PeriodName { get => _periodName; private set => Set(ref _periodName, value); }
        public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }
        public string BandLabel { get => _bandLabel; private set => Set(ref _bandLabel, value); }
        public string BandKind { get => _bandKind; private set => Set(ref _bandKind, value); }
        public string Note { get => _note; set => Set(ref _note, value); }
        public int PositiveCount { get => _positive; private set => Set(ref _positive, value); }
        public int NegativeCount { get => _negative; private set => Set(ref _negative, value); }
        public bool HasBehaviors => Behaviors.Count > 0;

        public ICommand SaveCommand { get; }
        public ICommand CompleteCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddPositiveCommand { get; }
        public ICommand AddNegativeCommand { get; }
        public Action<bool> RequestClose { private get; set; }

        private void Load()
        {
            EvaluationDetail d = _services.Performance.GetEvaluationDetail(_evaluationId);
            if (d == null) return;
            _evaluation = d.Evaluation;
            _scores = d.Scores.ToList();
            _employeeId = _evaluation.EmployeeId;
            EmployeeName = d.EmployeeName;
            EmployeeMeta = d.EmployeeMeta;
            PeriodName = d.PeriodName;
            Note = _evaluation.Note;

            EvalPeriod p = _services.Performance.GetPeriod(_evaluation.PeriodId);
            if (p != null) { _companyId = p.CompanyId; _periodStart = p.StartDate; _periodEnd = p.EndDate; }

            Criteria.Clear();
            foreach (EvaluationScore s in _scores)
                Criteria.Add(new CriterionCardViewModel(s, _services.Performance.ComputeLineScore, RecomputeTotal));

            LoadBehaviors(d.Behaviors, d.PositiveCount, d.NegativeCount);
            RecomputeTotal();
        }

        private void LoadBehaviors(IReadOnlyList<BehaviorEntry> entries, int pos, int neg)
        {
            Behaviors.Clear();
            foreach (BehaviorEntry b in entries) Behaviors.Add(new BehaviorItem(b));
            PositiveCount = pos; NegativeCount = neg;
            Raise(nameof(HasBehaviors));
        }

        private void RecomputeTotal()
        {
            decimal total = _services.Performance.ComputeTotal(_scores, _evaluation.WeightingMode);
            TotalText = total.ToString("0.#", L.Fr) + " / 100";
            ClassificationBand band = _services.Performance.Classify(total);
            BandLabel = PerfLabels.BandLabel(band);
            BandKind = PerfLabels.BandKind(band);
        }

        private void AddBehavior(bool positive)
        {
            var vm = new BehaviorQuickViewModel(_services, _companyId, _employeeId) { InitialPositive = positive };
            if (!PerfDialogs.Show(new Views.BehaviorQuickWindow { DataContext = vm }, vm)) return;
            var fresh = _services.Performance.GetBehaviors(_employeeId)
                .Where(b => b.OccurredAt >= _periodStart && b.OccurredAt <= _periodEnd).ToList();
            LoadBehaviors(fresh, fresh.Count(b => b.IsPositive), fresh.Count(b => !b.IsPositive));
        }

        private void Save(bool complete)
        {
            if (_evaluation == null) { RequestClose?.Invoke(false); return; }
            _evaluation.Note = Note;
            Result r = _services.Performance.SaveEvaluation(_evaluation, _scores);
            if (r.IsFailure) { Dialogs.Error(PerfDialogs.Err(r)); return; }
            if (complete)
            {
                Result c = _services.Performance.CompleteEvaluation(_evaluationId);
                if (c.IsFailure) { Dialogs.Error(PerfDialogs.Err(c)); return; }
            }
            RequestClose?.Invoke(true);
        }
    }

    /// <summary>One criterion card on the evaluation screen (a rating slider or a KPI target/achieved).</summary>
    public sealed class CriterionCardViewModel : ObservableObject
    {
        private readonly EvaluationScore _s;
        private readonly Func<EvaluationScore, decimal> _score;
        private readonly Action _changed;

        public CriterionCardViewModel(EvaluationScore s, Func<EvaluationScore, decimal> score, Action changed)
        {
            _s = s; _score = score; _changed = changed;
        }

        public string Name => _s.CriterionName;
        public string CategoryLabel => PerfLabels.CategoryLabel(_s.Category);
        public bool IsKpi => _s.Category == CriterionCategory.Kpi;
        public bool IsRating => !IsKpi;
        public double Max => _s.ScoreType == ScoreType.Stars5 ? 5 : (_s.ScoreType == ScoreType.Score20 ? 20 : 100);
        public double Tick => _s.ScoreType == ScoreType.Percent ? 5 : 1;
        public string ScaleSuffix => _s.ScoreType == ScoreType.Stars5 ? "/ 5" : (_s.ScoreType == ScoreType.Score20 ? "/ 20" : "%");

        public double RawValue
        {
            get => (double)(_s.RawValue ?? 0m);
            set { _s.RawValue = (decimal)Math.Round(value, 1); Recompute(); Raise(nameof(RawValueText)); }
        }
        public string RawValueText => (_s.RawValue ?? 0m).ToString("0.#", L.Fr) + " " + ScaleSuffix;

        public string TargetText => (_s.KpiTarget ?? 0m).ToString("0.###", L.Fr);
        public string ActualText
        {
            get => _s.KpiActual.HasValue ? _s.KpiActual.Value.ToString("0.###", L.Fr) : string.Empty;
            set { _s.KpiActual = Parse(value); Recompute(); }
        }
        public string AchievementText => IsKpi ? _s.NormalizedScore.ToString("0.#", L.Fr) + " %" : string.Empty;
        public string NormalizedText => _s.NormalizedScore.ToString("0.#", L.Fr);
        public string Note { get => _s.Note; set { _s.Note = value; Raise(nameof(Note)); } }

        private void Recompute()
        {
            _s.NormalizedScore = _score(_s);
            Raise(nameof(NormalizedText));
            Raise(nameof(AchievementText));
            _changed();
        }

        private static decimal? Parse(string s) =>
            decimal.TryParse(s, NumberStyles.Any, L.Fr, out decimal v) ? v : (decimal?)null;
    }

    public sealed class BehaviorItem
    {
        public BehaviorItem(BehaviorEntry b) { B = b; }
        public BehaviorEntry B { get; }
        public string Glyph => B.IsPositive ? "👍" : "👎";
        public string EmployeeName => B.EmployeeName;
        public string Note => B.Note;
        public string DateText => B.OccurredAt.ToString("dd/MM/yyyy", L.Fr);
        public string Kind => B.IsPositive ? "success" : "danger";
    }

    // ======================================================================
    //  TEMPLATE editor
    // ======================================================================
    public sealed class TemplateEditorViewModel : ObservableObject, IHasDialogResult
    {
        private readonly AppServices _services;
        private readonly long _companyId;
        private readonly long _templateId;
        private string _name = string.Empty, _department = string.Empty;
        private bool _weighted, _isDefault;

        public TemplateEditorViewModel(AppServices services, long companyId, long templateId)
        {
            _services = services; _companyId = companyId; _templateId = templateId;
            AddCriterionCommand = new RelayCommand(AddCriterion);
            RemoveCriterionCommand = new RelayCommand(o => Remove(o as TemplateCriterionRowViewModel));
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            if (templateId > 0)
            {
                TemplateDetail d = _services.Performance.GetTemplateDetail(templateId);
                if (d != null)
                {
                    _name = d.Template.Name; _department = d.Template.Department;
                    _weighted = d.Template.WeightingMode == WeightingMode.Weighted;
                    _isDefault = d.Template.IsDefault;
                    foreach (EvalCriterion c in d.Criteria) Criteria.Add(new TemplateCriterionRowViewModel(c, Recompute));
                }
            }
            if (Criteria.Count == 0) AddCriterion();
            Recompute();
        }

        public ObservableCollection<TemplateCriterionRowViewModel> Criteria { get; } = new ObservableCollection<TemplateCriterionRowViewModel>();

        public string Name { get => _name; set => Set(ref _name, value); }
        public string Department { get => _department; set => Set(ref _department, value); }
        public bool IsWeighted { get => _weighted; set { if (Set(ref _weighted, value)) { Raise(nameof(ShowWeights)); Recompute(); } } }
        public bool IsDefault { get => _isDefault; set => Set(ref _isDefault, value); }
        public bool ShowWeights => _weighted;

        private string _weightSum = string.Empty;
        public string WeightSumText { get => _weightSum; private set => Set(ref _weightSum, value); }
        private bool _weightOk = true;
        public bool WeightValid { get => _weightOk; private set => Set(ref _weightOk, value); }

        public ICommand AddCriterionCommand { get; }
        public ICommand RemoveCriterionCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public Action<bool> RequestClose { private get; set; }

        private void AddCriterion()
        {
            Criteria.Add(new TemplateCriterionRowViewModel(
                new EvalCriterion { Name = string.Empty, Category = CriterionCategory.Behavioral, ScoreType = ScoreType.Stars5, WeightPercent = 0m }, Recompute));
            Recompute();
        }

        private void Remove(TemplateCriterionRowViewModel row)
        {
            if (row != null) Criteria.Remove(row);
            Recompute();
        }

        private void Recompute()
        {
            decimal sum = Criteria.Sum(c => c.WeightValue);
            WeightSumText = string.Format(L.T("Perf_WeightSum"), sum.ToString("0.#", L.Fr));
            WeightValid = !_weighted || Math.Abs(sum - 100m) <= 0.5m;
        }

        private void Save()
        {
            var template = new EvalTemplate
            {
                Id = _templateId, CompanyId = _companyId, Name = _name, Department = _department,
                WeightingMode = _weighted ? WeightingMode.Weighted : WeightingMode.Simple, IsDefault = _isDefault
            };
            var criteria = Criteria.Select(c => c.ToEntity()).ToList();
            Result<long> r = _services.Performance.SaveTemplate(template, criteria);
            if (r.IsFailure) { Dialogs.Error(PerfDialogs.Err(r)); return; }
            RequestClose?.Invoke(true);
        }
    }

    public sealed class TemplateCriterionRowViewModel : ObservableObject
    {
        private readonly EvalCriterion _c;
        private readonly Action _changed;

        public TemplateCriterionRowViewModel(EvalCriterion c, Action changed)
        {
            _c = c; _changed = changed;
            SelectedCategory = c.Category;
            SelectedScoreType = c.ScoreType;
        }

        public static IReadOnlyList<CategoryOption> CategoryOptions { get; } = new[]
        {
            new CategoryOption { Value = CriterionCategory.Behavioral },
            new CategoryOption { Value = CriterionCategory.Technical },
            new CategoryOption { Value = CriterionCategory.Administrative },
            new CategoryOption { Value = CriterionCategory.Kpi }
        };
        public static IReadOnlyList<ScoreTypeOption> ScoreTypeOptions { get; } = new[]
        {
            new ScoreTypeOption { Value = ScoreType.Stars5 },
            new ScoreTypeOption { Value = ScoreType.Score20 },
            new ScoreTypeOption { Value = ScoreType.Percent }
        };

        public string Name { get => _c.Name; set { _c.Name = value; Raise(nameof(Name)); } }

        public CriterionCategory SelectedCategory
        {
            get => _c.Category;
            set { _c.Category = value; Raise(nameof(SelectedCategory)); Raise(nameof(IsKpi)); Raise(nameof(IsRating)); }
        }
        public ScoreType SelectedScoreType
        {
            get => _c.ScoreType;
            set { _c.ScoreType = value; Raise(nameof(SelectedScoreType)); }
        }

        public bool IsKpi => _c.Category == CriterionCategory.Kpi;
        public bool IsRating => !IsKpi;

        public string WeightText
        {
            get => _c.WeightPercent.ToString("0.#", L.Fr);
            set { _c.WeightPercent = decimal.TryParse(value, NumberStyles.Any, L.Fr, out decimal v) ? v : 0m; _changed(); }
        }
        public decimal WeightValue => _c.WeightPercent;

        public string TargetText
        {
            get => _c.KpiTarget.HasValue ? _c.KpiTarget.Value.ToString("0.###", L.Fr) : string.Empty;
            set { _c.KpiTarget = decimal.TryParse(value, NumberStyles.Any, L.Fr, out decimal v) ? v : (decimal?)null; }
        }

        public EvalCriterion ToEntity() => _c;
    }

    // ======================================================================
    //  QUICK behaviour (👍 / 👎)
    // ======================================================================
    public sealed class BehaviorQuickViewModel : ObservableObject, IHasDialogResult
    {
        private readonly AppServices _services;
        private readonly long _companyId;
        private EmployeePick _selectedEmployee;
        private bool _positive = true;
        private string _note = string.Empty;
        private DateTime _date = DateTime.Today;
        private readonly bool _lockedEmployee;

        public BehaviorQuickViewModel(AppServices services, long companyId, long employeeId)
        {
            _services = services; _companyId = companyId;
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            foreach (var e in _services.Employees.GetByCompany(companyId, false).OrderBy(x => x.LastNameFr))
                Employees.Add(new EmployeePick(e.Id, (e.LastNameFr + " " + e.FirstNameFr).Trim()));
            if (employeeId > 0)
            {
                _selectedEmployee = Employees.FirstOrDefault(e => e.Id == employeeId);
                _lockedEmployee = true;
            }
            else _selectedEmployee = Employees.FirstOrDefault();
        }

        public ObservableCollection<EmployeePick> Employees { get; } = new ObservableCollection<EmployeePick>();
        public EmployeePick SelectedEmployee { get => _selectedEmployee; set => Set(ref _selectedEmployee, value); }
        public bool ShowEmployeePicker => !_lockedEmployee;

        public bool InitialPositive { set => IsPositive = value; }
        public bool IsPositive { get => _positive; set { if (Set(ref _positive, value)) Raise(nameof(IsNegative)); } }
        public bool IsNegative { get => !_positive; set => IsPositive = !value; }
        public string Note { get => _note; set => Set(ref _note, value); }
        public DateTime OccurredAt { get => _date; set => Set(ref _date, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public Action<bool> RequestClose { private get; set; }

        private void Save()
        {
            if (_selectedEmployee == null) { Dialogs.Error(TranslationSource.Instance["Performance_EmployeeNotFound"]); return; }
            Result<long> r = _services.Performance.LogBehavior(_companyId, _selectedEmployee.Id, _positive, _note, _date);
            if (r.IsFailure) { Dialogs.Error(PerfDialogs.Err(r)); return; }
            RequestClose?.Invoke(true);
        }
    }
}
