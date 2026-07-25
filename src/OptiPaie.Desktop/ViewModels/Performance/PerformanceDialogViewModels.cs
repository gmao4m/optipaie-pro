using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels.Performance
{
    /// <summary>A checkable label/value item (departments in the cycle launcher, etc.).</summary>
    public sealed class CheckItemViewModel : ObservableObject
    {
        private bool _isChecked;
        public CheckItemViewModel(string label, string value, bool isChecked = false) { Label = label; Value = value; _isChecked = isChecked; }
        public string Label { get; }
        public string Value { get; }
        public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
    }

    /// <summary>A template group option for the default-template picker.</summary>
    public sealed class TemplateOption
    {
        public TemplateOption(string groupKey, string name) { GroupKey = groupKey; Name = name; }
        public string GroupKey { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    /// <summary>An enum option with a French label (for combo boxes).</summary>
    public sealed class LabeledOption<T>
    {
        public LabeledOption(T value, string label) { Value = value; Label = label; }
        public T Value { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    // ============================================================ Cycle launcher

    public sealed class CycleLaunchViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _companyId;

        private string _name;
        private LabeledOption<PerformanceCycleType> _cycleType;
        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today.AddMonths(3);
        private DateTime? _deadline = DateTime.Today.AddMonths(3).AddDays(-3);
        private int _periodYear = DateTime.Today.Year;
        private string _periodLabel = DateTime.Today.Year.ToString();
        private bool _selfAssessment;
        private TemplateOption _fallbackTemplate;

        public CycleLaunchViewModel(AppServices services, long companyId)
        {
            _services = services;
            _companyId = companyId;
            _name = "Campagne " + DateTime.Today.Year;

            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Monthly, "Mensuelle"));
            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Quarterly, "Trimestrielle"));
            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.SemiAnnual, "Semestrielle"));
            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Annual, "Annuelle"));
            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Probation, "Fin d'essai"));
            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Custom, "Personnalisée"));
            // Semi-annual is the recommended default cadence.
            _cycleType = CycleTypes.First(t => t.Value == PerformanceCycleType.SemiAnnual);

            // Period auto-defaults to the current semester (freely editable below).
            _periodLabel = (DateTime.Today.Month <= 6 ? "S1 " : "S2 ") + DateTime.Today.Year;

            foreach (string dept in _services.Employees.GetByCompany(companyId, false)
                         .Select(e => string.IsNullOrWhiteSpace(e.Department) ? "Non affecté" : e.Department.Trim())
                         .Distinct().OrderBy(d => d))
            {
                Departments.Add(new CheckItemViewModel(dept, dept, true));
            }

            foreach (TemplateSummary t in _services.Performance.GetTemplates(companyId)
                         .GroupBy(t => t.GroupKey).Select(g => g.First()))
            {
                Templates.Add(new TemplateOption(t.GroupKey, t.Name));
            }
            _fallbackTemplate = Templates.FirstOrDefault(t => t.GroupKey == "builtin-general") ?? Templates.FirstOrDefault();

            LaunchCommand = new RelayCommand(Launch);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }

        public ObservableCollection<LabeledOption<PerformanceCycleType>> CycleTypes { get; } = new ObservableCollection<LabeledOption<PerformanceCycleType>>();
        public ObservableCollection<CheckItemViewModel> Departments { get; } = new ObservableCollection<CheckItemViewModel>();
        public ObservableCollection<TemplateOption> Templates { get; } = new ObservableCollection<TemplateOption>();

        public string Name { get => _name; set => Set(ref _name, value); }
        public LabeledOption<PerformanceCycleType> CycleType { get => _cycleType; set => Set(ref _cycleType, value); }
        public DateTime StartDate { get => _startDate; set => Set(ref _startDate, value); }
        public DateTime EndDate { get => _endDate; set => Set(ref _endDate, value); }
        public DateTime? Deadline { get => _deadline; set => Set(ref _deadline, value); }
        public int PeriodYear { get => _periodYear; set => Set(ref _periodYear, value); }
        public string PeriodLabel { get => _periodLabel; set => Set(ref _periodLabel, value); }
        public bool SelfAssessment { get => _selfAssessment; set => Set(ref _selfAssessment, value); }
        public TemplateOption FallbackTemplate { get => _fallbackTemplate; set => Set(ref _fallbackTemplate, value); }

        public string Hint => "Chaque employé reçoit automatiquement le modèle et l'évaluateur par défaut de son département " +
                              "(sinon le modèle choisi ci-dessous).";

        public ICommand LaunchCommand { get; }
        public ICommand CancelCommand { get; }

        private void Launch()
        {
            List<string> depts = Departments.Where(d => d.IsChecked).Select(d => d.Value).ToList();
            if (depts.Count == 0) { Dialogs.Info("Sélectionnez au moins un département."); return; }

            var request = new CycleLaunchRequest
            {
                CompanyId = _companyId,
                Name = _name,
                CycleType = _cycleType.Value,
                StartDate = _startDate,
                EndDate = _endDate,
                Deadline = _deadline,
                SelfAssessment = _selfAssessment,
                PeriodYear = _periodYear,
                PeriodLabel = _periodLabel,
                Departments = depts,
                DefaultTemplateGroupKey = _fallbackTemplate?.GroupKey
            };

            Result<long> r = _services.Performance.LaunchCycle(request);
            if (r.IsFailure) { Dialogs.Error(r.Error); return; }
            RequestClose?.Invoke(true);
        }
    }

    // ============================================================ Template editor

    public sealed class TemplateCriterionRowViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private readonly Action _changed;
        private string _label;
        private decimal _weight;
        private bool _isKpi;
        private bool _higherIsBetter;
        private string _targetText;

        public TemplateCriterionRowViewModel(Action changed, string label = "", decimal weight = 0m,
            CriterionType type = CriterionType.Behavioral, bool higherIsBetter = true, decimal? defaultTarget = null)
        {
            _changed = changed;
            _label = label;
            _weight = weight;
            _isKpi = type == CriterionType.Kpi;
            _higherIsBetter = higherIsBetter;
            _targetText = defaultTarget.HasValue ? defaultTarget.Value.ToString("0.##", Fr) : string.Empty;
        }

        public string Label { get => _label; set => Set(ref _label, value); }
        public decimal Weight { get => _weight; set { if (Set(ref _weight, value)) _changed?.Invoke(); } }

        /// <summary>KPI (numeric target/achieved) vs behavioral (rated 1-5).</summary>
        public bool IsKpi { get => _isKpi; set { if (Set(ref _isKpi, value)) Raise(nameof(ShowKpiOptions)); } }
        public bool ShowKpiOptions => _isKpi;
        public bool HigherIsBetter { get => _higherIsBetter; set => Set(ref _higherIsBetter, value); }

        /// <summary>Optional default target that pre-fills a review (KPI only).</summary>
        public string TargetText { get => _targetText; set => Set(ref _targetText, value); }

        public CriterionType Type => _isKpi ? CriterionType.Kpi : CriterionType.Behavioral;

        public decimal? DefaultTarget
        {
            get
            {
                if (!_isKpi || string.IsNullOrWhiteSpace(_targetText)) return null;
                string cleaned = _targetText.Replace(" ", string.Empty).Replace(",", ".");
                return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v) ? v : (decimal?)null;
            }
        }
    }

    public sealed class TemplateEditorViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private readonly AppServices _services;
        private readonly long _companyId;
        private readonly bool _duplicate;
        private long _templateId;
        private string _groupKey;

        private string _name;
        private string _description;
        private string _departmentTag;
        private decimal _scaleMax = 20m;
        private string _weightTotalText = "0 %";
        private bool _weightBalanced;
        private bool _simpleMode;

        public TemplateEditorViewModel(AppServices services, long companyId, long sourceTemplateId, bool duplicate)
        {
            _services = services;
            _companyId = companyId;
            _duplicate = duplicate;
            _templateId = duplicate ? 0 : sourceTemplateId;

            TemplateDetail detail = _services.Performance.GetTemplateDetail(sourceTemplateId);
            if (detail != null)
            {
                _name = duplicate ? detail.Template.Name + " (copie)" : detail.Template.Name;
                _description = detail.Template.Description;
                _departmentTag = detail.Template.DepartmentTag;
                _scaleMax = detail.Template.ScaleMax;
                _groupKey = duplicate ? null : detail.Template.GroupKey;
                foreach (PerformanceTemplateCriterion c in detail.Criteria)
                {
                    Criteria.Add(new TemplateCriterionRowViewModel(Recompute, c.Label, c.WeightPercent,
                        c.CriterionType, c.HigherIsBetter, c.DefaultTarget));
                }
            }

            Title = duplicate ? "Dupliquer le modèle" : "Modifier le modèle";
            AddCriterionCommand = new RelayCommand(AddCriterion);
            RemoveCriterionCommand = new RelayCommand(RemoveCriterion);
            NormalizeCommand = new RelayCommand(Normalize);
            EqualizeCommand = new RelayCommand(() => { EqualizeWeights(); Recompute(); });
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            Recompute();
        }

        public Action<bool> RequestClose { get; set; }
        public string Title { get; }
        public ObservableCollection<TemplateCriterionRowViewModel> Criteria { get; } = new ObservableCollection<TemplateCriterionRowViewModel>();

        public string Name { get => _name; set => Set(ref _name, value); }
        public string Description { get => _description; set => Set(ref _description, value); }
        public string DepartmentTag { get => _departmentTag; set => Set(ref _departmentTag, value); }
        public decimal ScaleMax { get => _scaleMax; set => Set(ref _scaleMax, value); }
        public string WeightTotalText { get => _weightTotalText; private set => Set(ref _weightTotalText, value); }

        /// <summary>True when the weights sum to 100 % (± 0.5); drives the colour feedback.</summary>
        public bool WeightBalanced { get => _weightBalanced; private set => Set(ref _weightBalanced, value); }

        /// <summary>Simple mode: all criteria share an equal weight (no manual weight editing).</summary>
        public bool SimpleMode
        {
            get => _simpleMode;
            set
            {
                if (Set(ref _simpleMode, value))
                {
                    if (value) EqualizeWeights();
                    Recompute();
                    Raise(nameof(WeightsEditable));
                }
            }
        }

        public bool WeightsEditable => !_simpleMode;

        public ICommand AddCriterionCommand { get; }
        public ICommand RemoveCriterionCommand { get; }
        public ICommand NormalizeCommand { get; }
        public ICommand EqualizeCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void AddCriterion()
        {
            Criteria.Add(new TemplateCriterionRowViewModel(Recompute, "Nouveau critère", 0m));
            if (_simpleMode) EqualizeWeights();
            Recompute();
        }

        private void RemoveCriterion(object p)
        {
            if (p is TemplateCriterionRowViewModel r)
            {
                Criteria.Remove(r);
                if (_simpleMode) EqualizeWeights();
                Recompute();
            }
        }

        /// <summary>Scales the current weights proportionally so they sum to exactly 100 %.</summary>
        private void Normalize()
        {
            if (Criteria.Count == 0) return;
            decimal total = Criteria.Sum(c => c.Weight);
            if (total <= 0m) { EqualizeWeights(); Recompute(); return; }

            decimal acc = 0m;
            for (int k = 0; k < Criteria.Count; k++)
            {
                if (k == Criteria.Count - 1) Criteria[k].Weight = Math.Round(100m - acc, 2, MidpointRounding.AwayFromZero);
                else { decimal w = Math.Round(Criteria[k].Weight / total * 100m, 2, MidpointRounding.AwayFromZero); Criteria[k].Weight = w; acc += w; }
            }
            Recompute();
        }

        /// <summary>Gives every criterion the same weight (100 / n), with any rounding on the last.</summary>
        private void EqualizeWeights()
        {
            int n = Criteria.Count;
            if (n == 0) return;
            decimal each = Math.Round(100m / n, 2, MidpointRounding.AwayFromZero);
            decimal acc = 0m;
            for (int k = 0; k < n; k++)
            {
                if (k == n - 1) Criteria[k].Weight = Math.Round(100m - acc, 2, MidpointRounding.AwayFromZero);
                else { Criteria[k].Weight = each; acc += each; }
            }
        }

        private void Recompute()
        {
            decimal total = Criteria.Sum(c => c.Weight);
            WeightBalanced = Math.Abs(total - 100m) <= 0.5m;
            WeightTotalText = "Total des poids : " + total.ToString("0.##", Fr) + " % / 100 %";
        }

        private void Save()
        {
            var template = new PerformanceTemplate
            {
                Id = _templateId,
                CompanyId = _companyId,
                GroupKey = _groupKey,
                Name = _name,
                Description = _description,
                DepartmentTag = _departmentTag,
                Kind = TemplateKind.Custom,
                ScaleMax = _scaleMax <= 0m ? 20m : _scaleMax
            };
            var criteria = Criteria.Select(c => new PerformanceTemplateCriterion
            {
                Label = c.Label,
                WeightPercent = c.Weight,
                CriterionType = c.Type,
                HigherIsBetter = c.HigherIsBetter,
                DefaultTarget = c.DefaultTarget
            }).ToList();

            Result<long> r = _services.Performance.SaveTemplate(template, criteria);
            if (r.IsFailure) { Dialogs.Error(r.Error); return; }
            RequestClose?.Invoke(true);
        }
    }

    // ================================================================= Goal editor

    public sealed class GoalEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _goalId;

        private Employee _employee;
        private string _title;
        private string _targetMetric;
        private DateTime? _dueDate = DateTime.Today.AddMonths(3);
        private decimal _progress;
        private LabeledOption<PerformanceGoalStatus> _status;

        public GoalEditViewModel(AppServices services, long companyId, GoalRow existing)
        {
            _services = services;

            foreach (Employee e in _services.Employees.GetByCompany(companyId, false).OrderBy(e => e.LastNameFr))
            {
                Employees.Add(e);
            }

            Statuses.Add(new LabeledOption<PerformanceGoalStatus>(PerformanceGoalStatus.Active, "En cours"));
            Statuses.Add(new LabeledOption<PerformanceGoalStatus>(PerformanceGoalStatus.Achieved, "Atteint"));
            Statuses.Add(new LabeledOption<PerformanceGoalStatus>(PerformanceGoalStatus.Missed, "Manqué"));
            Statuses.Add(new LabeledOption<PerformanceGoalStatus>(PerformanceGoalStatus.Cancelled, "Annulé"));

            if (existing != null)
            {
                _goalId = existing.GoalId;
                _employee = Employees.FirstOrDefault(e => e.Id == existing.EmployeeId);
                _title = existing.Title;
                _targetMetric = existing.TargetMetric;
                _dueDate = existing.DueDate;
                _progress = existing.ProgressPercent;
                _status = Statuses.FirstOrDefault(s => s.Value == existing.Status) ?? Statuses[0];
                Title = "Modifier l'objectif";
            }
            else
            {
                _employee = Employees.FirstOrDefault();
                _status = Statuses[0];
                Title = "Nouvel objectif";
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }
        public string Title { get; }
        public bool CanChooseEmployee => _goalId == 0;
        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<LabeledOption<PerformanceGoalStatus>> Statuses { get; } = new ObservableCollection<LabeledOption<PerformanceGoalStatus>>();

        public Employee Employee { get => _employee; set => Set(ref _employee, value); }
        public string GoalTitle { get => _title; set => Set(ref _title, value); }
        public string TargetMetric { get => _targetMetric; set => Set(ref _targetMetric, value); }
        public DateTime? DueDate { get => _dueDate; set => Set(ref _dueDate, value); }
        public decimal Progress { get => _progress; set => Set(ref _progress, value); }
        public double ProgressValue { get => (double)_progress; set { Progress = (decimal)Math.Round(value); Raise(nameof(Progress)); } }
        public LabeledOption<PerformanceGoalStatus> Status { get => _status; set => Set(ref _status, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            if (_employee == null) { Dialogs.Error("Sélectionnez un employé."); return; }

            var goal = new PerformanceGoal
            {
                Id = _goalId,
                EmployeeId = _employee.Id,
                Title = _title,
                TargetMetric = _targetMetric,
                DueDate = _dueDate,
                ProgressPercent = _progress,
                Status = _status.Value
            };

            Result r = _goalId == 0
                ? ToResult(_services.Performance.CreateGoal(goal))
                : _services.Performance.UpdateGoal(goal);
            if (r.IsFailure) { Dialogs.Error(r.Error); return; }
            RequestClose?.Invoke(true);
        }

        private static Result ToResult<T>(Result<T> r) => r.IsSuccess ? Result.Ok() : Result.Fail(r.Error, r.ErrorCode);
    }

    // ============================================================ Career event

    public sealed class CareerEventViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _employeeId;

        private string _oldPosition;
        private string _newPosition;
        private string _amountText;
        private string _category;
        private DateTime _date = DateTime.Today;
        private string _reason;

        public CareerEventViewModel(AppServices services, long employeeId, string employeeName, bool isPromotion)
        {
            _services = services;
            _employeeId = employeeId;
            IsPromotion = isPromotion;
            EmployeeName = employeeName;
            Title = isPromotion ? "Enregistrer une promotion" : "Enregistrer une récompense";

            if (isPromotion)
            {
                Employee e = _services.Employees.Get(employeeId);
                _oldPosition = e?.Poste;
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }
        public string Title { get; }
        public string EmployeeName { get; }
        public bool IsPromotion { get; }
        public bool IsReward => !IsPromotion;

        public string OldPosition { get => _oldPosition; set => Set(ref _oldPosition, value); }
        public string NewPosition { get => _newPosition; set => Set(ref _newPosition, value); }
        public string AmountText { get => _amountText; set => Set(ref _amountText, value); }
        public string Category { get => _category; set => Set(ref _category, value); }
        public DateTime EventDate { get => _date; set => Set(ref _date, value); }
        public string Reason { get => _reason; set => Set(ref _reason, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            Result result;
            if (IsPromotion)
            {
                result = ToResult(_services.Performance.LogPromotion(_employeeId, _oldPosition, _newPosition, _date, _reason, null));
            }
            else
            {
                decimal amount = 0m;
                if (!string.IsNullOrWhiteSpace(_amountText))
                {
                    decimal.TryParse(_amountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
                }
                result = ToResult(_services.Performance.LogReward(_employeeId, amount, _category, _date, _reason));
            }

            if (result.IsFailure) { Dialogs.Error(result.Error); return; }
            RequestClose?.Invoke(true);
        }

        private static Result ToResult<T>(Result<T> r) => r.IsSuccess ? Result.Ok() : Result.Fail(r.Error, r.ErrorCode);
    }
}
