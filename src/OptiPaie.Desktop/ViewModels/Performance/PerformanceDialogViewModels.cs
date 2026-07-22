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

            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Quarterly, "Trimestrielle"));
            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Annual, "Annuelle"));
            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Probation, "Fin d'essai"));
            CycleTypes.Add(new LabeledOption<PerformanceCycleType>(PerformanceCycleType.Custom, "Personnalisée"));
            _cycleType = CycleTypes[0];

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
        private readonly Action _changed;
        private string _label;
        private decimal _weight;
        public TemplateCriterionRowViewModel(Action changed, string label = "", decimal weight = 0m)
        {
            _changed = changed; _label = label; _weight = weight;
        }
        public string Label { get => _label; set => Set(ref _label, value); }
        public decimal Weight { get => _weight; set { if (Set(ref _weight, value)) _changed?.Invoke(); } }
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
                    Criteria.Add(new TemplateCriterionRowViewModel(Recompute, c.Label, c.WeightPercent));
                }
            }

            Title = duplicate ? "Dupliquer le modèle" : "Modifier le modèle";
            AddCriterionCommand = new RelayCommand(() => { Criteria.Add(new TemplateCriterionRowViewModel(Recompute, "Nouveau critère", 0m)); Recompute(); });
            RemoveCriterionCommand = new RelayCommand(p => { if (p is TemplateCriterionRowViewModel r) { Criteria.Remove(r); Recompute(); } });
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

        public ICommand AddCriterionCommand { get; }
        public ICommand RemoveCriterionCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Recompute()
        {
            decimal total = Criteria.Sum(c => c.Weight);
            WeightTotalText = "Total des poids : " + total.ToString("0.##", Fr) + " % (doit faire 100 %)";
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
            var criteria = Criteria.Select(c => new PerformanceTemplateCriterion { Label = c.Label, WeightPercent = c.Weight }).ToList();

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
