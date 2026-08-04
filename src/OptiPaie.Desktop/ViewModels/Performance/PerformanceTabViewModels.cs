using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels.Performance
{
    internal static class L
    {
        public static string T(string key) => TranslationSource.Instance[key];
        public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    }

    // ======================================================================
    //  TAB 1 — EVALUATIONS (the board for a period)
    // ======================================================================
    public sealed class EvaluationsTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private long _companyId;
        private PeriodSummary _selectedPeriod;
        private string _statusMessage = string.Empty, _bestText = string.Empty;
        private bool _hasBest;

        public EvaluationsTabViewModel(AppServices services)
        {
            _services = services;
            NewPeriodCommand = new RelayCommand(NewPeriod);
            TogglePeriodCommand = new RelayCommand(TogglePeriod, () => _selectedPeriod != null);
            DeletePeriodCommand = new RelayCommand(DeletePeriod, () => _selectedPeriod != null);
            EvaluateCommand = new RelayCommand(o => Evaluate(o as BoardRowViewModel));
            QuickBehaviorCommand = new RelayCommand(QuickBehavior);
            QuickPositiveCommand = new RelayCommand(o => QuickFor(o as BoardRowViewModel, true));
            QuickNegativeCommand = new RelayCommand(o => QuickFor(o as BoardRowViewModel, false));
            RefreshCommand = new RelayCommand(Refresh);
        }

        public ObservableCollection<PeriodSummary> Periods { get; } = new ObservableCollection<PeriodSummary>();
        public ObservableCollection<BoardRowViewModel> Board { get; } = new ObservableCollection<BoardRowViewModel>();

        public PeriodSummary SelectedPeriod
        {
            get => _selectedPeriod;
            set { if (Set(ref _selectedPeriod, value)) { LoadBoard(); Raise(nameof(PeriodActionLabel)); } }
        }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public string BestText { get => _bestText; private set => Set(ref _bestText, value); }
        public bool HasBest { get => _hasBest; private set => Set(ref _hasBest, value); }

        public string PeriodActionLabel =>
            _selectedPeriod != null && _selectedPeriod.Status == PeriodStatus.Closed
                ? L.T("Perf_Reopen") : L.T("Perf_Close");

        public ICommand NewPeriodCommand { get; }
        public ICommand TogglePeriodCommand { get; }
        public ICommand DeletePeriodCommand { get; }
        public ICommand EvaluateCommand { get; }
        public ICommand QuickBehaviorCommand { get; }
        public ICommand QuickPositiveCommand { get; }
        public ICommand QuickNegativeCommand { get; }
        public ICommand RefreshCommand { get; }

        public void SetCompany(long companyId) { _companyId = companyId; Refresh(); }

        /// <summary>Selects a period by id (used by the Overview's "handle" actions).</summary>
        public void SelectPeriod(long periodId)
        {
            PeriodSummary match = Periods.FirstOrDefault(p => p.PeriodId == periodId);
            if (match != null) SelectedPeriod = match;
        }

        private void QuickFor(BoardRowViewModel row, bool positive)
        {
            if (row == null) return;
            var vm = new BehaviorQuickViewModel(_services, _companyId, row.Row.EmployeeId) { InitialPositive = positive };
            if (PerfDialogs.Show(new BehaviorQuickWindow { DataContext = vm }, vm)) LoadBest();
        }

        private void Refresh()
        {
            Periods.Clear();
            foreach (PeriodSummary p in _services.Performance.GetPeriods(_companyId)) Periods.Add(p);
            SelectedPeriod = Periods.FirstOrDefault();
            if (_selectedPeriod == null) LoadBoard();
            LoadBest();
        }

        private void LoadBoard()
        {
            Board.Clear();
            if (_selectedPeriod == null) { StatusMessage = string.Empty; return; }
            var rows = _services.Performance.GetEvaluationBoard(_selectedPeriod.PeriodId);
            foreach (EvaluationSummary r in rows) Board.Add(new BoardRowViewModel(r));
            int done = rows.Count(r => r.Status == EvaluationStatus.Done);
            StatusMessage = string.Format(L.T("Perf_Board_Status"), done, rows.Count);
        }

        private void LoadBest()
        {
            BestEmployeeInfo best = _services.Performance.GetBestEmployee(_companyId);
            HasBest = best != null;
            BestText = best == null ? string.Empty
                : string.Format(L.T("Perf_BestFormat"), best.EmployeeName, best.Score.ToString("0.#", L.Fr), best.PeriodName);
        }

        private void NewPeriod()
        {
            var vm = new PeriodEditViewModel(_services, _companyId);
            if (PerfDialogs.Show(new PeriodEditWindow { DataContext = vm }, vm)) Refresh();
        }

        private void TogglePeriod()
        {
            if (_selectedPeriod == null) return;
            Result r = _selectedPeriod.Status == PeriodStatus.Closed
                ? _services.Performance.ReopenPeriod(_selectedPeriod.PeriodId)
                : _services.Performance.ClosePeriod(_selectedPeriod.PeriodId);
            if (r.IsFailure) Dialogs.Error(Err(r)); else Refresh();
        }

        private void DeletePeriod()
        {
            if (_selectedPeriod == null) return;
            if (!Dialogs.Confirm(string.Format(L.T("Perf_ConfirmDeletePeriod"), _selectedPeriod.Name))) return;
            Result r = _services.Performance.DeletePeriod(_selectedPeriod.PeriodId);
            if (r.IsFailure) Dialogs.Error(Err(r)); else Refresh();
        }

        private void Evaluate(BoardRowViewModel row)
        {
            if (row == null || _selectedPeriod == null) return;
            long evalId = row.Row.EvaluationId;
            if (evalId == 0)
            {
                Result<long> created = _services.Performance.CreateEvaluation(_selectedPeriod.PeriodId, row.Row.EmployeeId, null);
                if (created.IsFailure) { Dialogs.Error(Err(created)); return; }
                evalId = created.Value;
            }
            var vm = new EvaluationFormViewModel(_services, evalId);
            var window = new EvaluationFormWindow { DataContext = vm };
            PerfDialogs.Show(window, vm);
            LoadBoard();
            LoadBest();
        }

        private void QuickBehavior()
        {
            var vm = new BehaviorQuickViewModel(_services, _companyId, 0);
            PerfDialogs.Show(new BehaviorQuickWindow { DataContext = vm }, vm);
        }

        private static string Err(Result r) =>
            string.IsNullOrEmpty(r.ErrorCode) ? r.Error : TranslationSource.Instance[r.ErrorCode];
    }

    /// <summary>One employee row on the evaluations board.</summary>
    public sealed class BoardRowViewModel
    {
        public BoardRowViewModel(EvaluationSummary r) { Row = r; }
        public EvaluationSummary Row { get; }
        public string EmployeeName => Row.EmployeeName;
        public string Department => Row.Department;
        public bool IsEvaluated => Row.Status == EvaluationStatus.Done;
        public string ScoreText => IsEvaluated ? Row.TotalScore.ToString("0.#", L.Fr) + " / 100" : "—";
        public string BandLabel => IsEvaluated ? PerfLabels.BandLabel(Row.Band) : string.Empty;
        public string BandKind => IsEvaluated ? PerfLabels.BandKind(Row.Band) : "neutral";
        public string StatusLabel => PerfLabels.EvalStatusLabel(Row.Status);
        public string StatusKind => PerfLabels.EvalStatusKind(Row.Status);
        public string ActionLabel => IsEvaluated ? L.T("Perf_Open") : L.T("Perf_Evaluate");
    }

    // ======================================================================
    //  TAB 2 — TEMPLATES
    // ======================================================================
    public sealed class TemplatesTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private long _companyId;
        private TemplateRowViewModel _selected;

        public TemplatesTabViewModel(AppServices services)
        {
            _services = services;
            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit, () => _selected != null);
            DuplicateCommand = new RelayCommand(Duplicate, () => _selected != null);
            DeleteCommand = new RelayCommand(Delete, () => _selected != null && !_selected.IsBuiltIn);
            SetDefaultCommand = new RelayCommand(SetDefault, () => _selected != null && !_selected.IsBuiltIn);
            RefreshCommand = new RelayCommand(Refresh);
        }

        public ObservableCollection<TemplateRowViewModel> Templates { get; } = new ObservableCollection<TemplateRowViewModel>();

        public TemplateRowViewModel Selected
        {
            get => _selected;
            set => Set(ref _selected, value);
        }

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DuplicateCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand SetDefaultCommand { get; }
        public ICommand RefreshCommand { get; }

        public void SetCompany(long companyId) { _companyId = companyId; Refresh(); }

        private void Refresh()
        {
            Templates.Clear();
            foreach (TemplateSummary t in _services.Performance.GetTemplates(_companyId))
                Templates.Add(new TemplateRowViewModel(t));
        }

        private void New()
        {
            var vm = new TemplateEditorViewModel(_services, _companyId, 0);
            if (PerfDialogs.Show(new TemplateEditorWindow { DataContext = vm }, vm)) Refresh();
        }

        private void Edit()
        {
            if (_selected == null) return;
            if (_selected.IsBuiltIn) { Duplicate(); return; }
            var vm = new TemplateEditorViewModel(_services, _companyId, _selected.T.TemplateId);
            if (PerfDialogs.Show(new TemplateEditorWindow { DataContext = vm }, vm)) Refresh();
        }

        private void Duplicate()
        {
            if (_selected == null) return;
            Result<long> r = _services.Performance.DuplicateTemplate(_selected.T.TemplateId, _companyId,
                _selected.Name + " (copie)", _selected.T.Department);
            if (r.IsFailure) { Dialogs.Error(Err(r)); return; }
            Refresh();
            var vm = new TemplateEditorViewModel(_services, _companyId, r.Value);
            if (PerfDialogs.Show(new TemplateEditorWindow { DataContext = vm }, vm)) Refresh();
        }

        private void Delete()
        {
            if (_selected == null) return;
            if (!Dialogs.Confirm(string.Format(L.T("Perf_ConfirmDeleteTemplate"), _selected.Name))) return;
            Result r = _services.Performance.DeleteTemplate(_selected.T.TemplateId);
            if (r.IsFailure) Dialogs.Error(Err(r)); else Refresh();
        }

        private void SetDefault()
        {
            if (_selected == null) return;
            Result r = _services.Performance.SetDefaultTemplate(_companyId, _selected.T.TemplateId);
            if (r.IsFailure) Dialogs.Error(Err(r)); else Refresh();
        }

        private static string Err(Result r) =>
            string.IsNullOrEmpty(r.ErrorCode) ? r.Error : TranslationSource.Instance[r.ErrorCode];
    }

    public sealed class TemplateRowViewModel
    {
        public TemplateRowViewModel(TemplateSummary t) { T = t; }
        public TemplateSummary T { get; }
        public string Name => T.Name;
        public string Department => string.IsNullOrEmpty(T.Department) ? L.T("Perf_AllDepartments") : T.Department;
        public string ModeLabel => PerfLabels.WeightingLabel(T.WeightingMode);
        public string CountText => T.CriteriaCount.ToString(L.Fr);
        public bool IsBuiltIn => T.IsBuiltIn;
        public bool IsDefault => T.IsDefault;
        public string TagLabel => T.IsBuiltIn ? L.T("Perf_BuiltIn") : (T.IsDefault ? L.T("Perf_Default") : string.Empty);
        public string TagKind => T.IsBuiltIn ? "neutral" : "accent";
        public bool HasTag => T.IsBuiltIn || T.IsDefault;
    }

    // ======================================================================
    //  TAB 3 — REPORTS
    // ======================================================================
    public sealed class ReportsTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private long _companyId;
        private int _mode; // 0 general, 1 department, 2 employee
        private string _selectedDepartment;
        private EmployeePick _selectedEmployee;
        private string _headline = string.Empty, _subline = string.Empty;
        private GeneralReport _general;
        private DeptReport _dept;
        private EmployeeReport _employee;

        public ReportsTabViewModel(AppServices services)
        {
            _services = services;
            ExportPdfCommand = new RelayCommand(ExportPdf, () => _companyId > 0);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => _companyId > 0);
        }

        public ObservableCollection<string> Departments { get; } = new ObservableCollection<string>();
        public ObservableCollection<EmployeePick> Employees { get; } = new ObservableCollection<EmployeePick>();

        // shared display collections (repopulated per mode)
        public ObservableCollection<DeptScoreRow> DeptAverages { get; } = new ObservableCollection<DeptScoreRow>();
        public ObservableCollection<RankItem> TopRows { get; } = new ObservableCollection<RankItem>();
        public ObservableCollection<RankItem> SupportRows { get; } = new ObservableCollection<RankItem>();
        public ObservableCollection<TrendPoint> TrendRows { get; } = new ObservableCollection<TrendPoint>();
        public ObservableCollection<CriterionScore> StrengthRows { get; } = new ObservableCollection<CriterionScore>();
        public ObservableCollection<CriterionScore> WeaknessRows { get; } = new ObservableCollection<CriterionScore>();

        public int Mode { get => _mode; set { if (Set(ref _mode, value)) { RaiseModes(); Build(); } } }
        public bool IsGeneral => _mode == 0;
        public bool IsDept => _mode == 1;
        public bool IsEmployee => _mode == 2;

        public string SelectedDepartment { get => _selectedDepartment; set { if (Set(ref _selectedDepartment, value)) Build(); } }
        public EmployeePick SelectedEmployee { get => _selectedEmployee; set { if (Set(ref _selectedEmployee, value)) Build(); } }

        public string Headline { get => _headline; private set => Set(ref _headline, value); }
        public string Subline { get => _subline; private set => Set(ref _subline, value); }

        // general scalars
        private string _companyAvg = "—", _bestText = string.Empty;
        public string CompanyAvgText { get => _companyAvg; private set => Set(ref _companyAvg, value); }
        public string BestText { get => _bestText; private set => Set(ref _bestText, value); }

        // employee scalars
        private string _empScore = "—", _empBand = string.Empty, _empReco = string.Empty, _empBehavior = string.Empty;
        public string EmpScoreText { get => _empScore; private set => Set(ref _empScore, value); }
        public string EmpBandText { get => _empBand; private set => Set(ref _empBand, value); }
        public string EmpRecommendation { get => _empReco; private set => Set(ref _empReco, value); }
        public string EmpBehaviorText { get => _empBehavior; private set => Set(ref _empBehavior, value); }

        public ICommand ExportPdfCommand { get; }
        public ICommand ExportCsvCommand { get; }

        public void SetCompany(long companyId)
        {
            _companyId = companyId;
            Departments.Clear();
            foreach (string d in _services.Departments.GetNamesForCompany(companyId)) Departments.Add(d);
            _selectedDepartment = Departments.FirstOrDefault(); Raise(nameof(SelectedDepartment));
            Employees.Clear();
            foreach (var e in _services.Employees.GetByCompany(companyId, false).OrderBy(x => x.LastNameFr))
                Employees.Add(new EmployeePick(e.Id, (e.LastNameFr + " " + e.FirstNameFr).Trim()));
            _selectedEmployee = Employees.FirstOrDefault(); Raise(nameof(SelectedEmployee));
            Build();
        }

        /// <summary>Opens the per-employee report (used by the Overview's decline "handle" action).</summary>
        public void ShowEmployee(long employeeId)
        {
            _selectedEmployee = Employees.FirstOrDefault(e => e.Id == employeeId) ?? _selectedEmployee;
            Raise(nameof(SelectedEmployee));
            if (_mode != 2) { Mode = 2; } else { Build(); }
        }

        private void RaiseModes() { Raise(nameof(IsGeneral)); Raise(nameof(IsDept)); Raise(nameof(IsEmployee)); }

        private void Build()
        {
            DeptAverages.Clear(); TopRows.Clear(); SupportRows.Clear(); TrendRows.Clear();
            StrengthRows.Clear(); WeaknessRows.Clear();

            if (_mode == 0) BuildGeneral();
            else if (_mode == 1) BuildDept();
            else BuildEmployee();
        }

        private void BuildGeneral()
        {
            _general = _services.Performance.GetGeneralReport(_companyId);
            Headline = L.T("Perf_Report_General");
            if (!_general.HasData) { Subline = L.T("Perf_NoData"); CompanyAvgText = "—"; BestText = string.Empty; return; }
            Subline = string.Format(L.T("Perf_Report_GeneralSub"), _general.EvaluatedCount, _general.EmployeeCount);
            CompanyAvgText = _general.CompanyAverage.ToString("0.#", L.Fr) + " / 100";
            BestText = _general.BestEmployee == null ? string.Empty
                : string.Format(L.T("Perf_BestFormat"), _general.BestEmployee.EmployeeName,
                    _general.BestEmployee.Score.ToString("0.#", L.Fr), _general.BestEmployee.PeriodName);
            foreach (var d in _general.Departments) DeptAverages.Add(d);
            foreach (var r in _general.TopPerformers) TopRows.Add(new RankItem(r));
            foreach (var r in _general.NeedSupport) SupportRows.Add(new RankItem(r));
            foreach (var t in _general.Trend) TrendRows.Add(t);
        }

        private void BuildDept()
        {
            _dept = _services.Performance.GetDeptReport(_companyId, _selectedDepartment ?? string.Empty);
            Headline = string.Format(L.T("Perf_Report_DeptTitle"), _selectedDepartment);
            CompanyAvgText = _dept.AverageScore.ToString("0.#", L.Fr) + " / 100";
            Subline = string.Format(L.T("Perf_Report_DeptSub"), _dept.Ranking.Count, _dept.EmployeeCount);
            BestText = string.Empty;
            foreach (var r in _dept.Ranking) TopRows.Add(new RankItem(r));
            foreach (var r in _dept.NeedSupport) SupportRows.Add(new RankItem(r));
        }

        private void BuildEmployee()
        {
            if (_selectedEmployee == null) { Headline = L.T("Perf_Report_Employee"); Subline = L.T("Perf_NoData"); return; }
            _employee = _services.Performance.GetEmployeeReport(_selectedEmployee.Id);
            Headline = _employee.EmployeeName;
            Subline = PerfLabels.RecommendationLabel(_employee.RecommendationKey);
            if (!_employee.HasData)
            {
                EmpScoreText = "—"; EmpBandText = string.Empty;
                EmpRecommendation = PerfLabels.RecommendationLabel(_employee.RecommendationKey);
                EmpBehaviorText = string.Format(L.T("Perf_BehaviorCount"), _employee.PositiveBehaviors, _employee.NegativeBehaviors);
                return;
            }
            EmpScoreText = _employee.LatestScore.ToString("0.#", L.Fr) + " / 100";
            EmpBandText = PerfLabels.BandLabel(_employee.LatestBand);
            EmpRecommendation = PerfLabels.RecommendationLabel(_employee.RecommendationKey);
            EmpBehaviorText = string.Format(L.T("Perf_BehaviorCount"), _employee.PositiveBehaviors, _employee.NegativeBehaviors);
            foreach (var t in _employee.Trend) TrendRows.Add(t);
            foreach (var s in _employee.Strengths) StrengthRows.Add(s);
            foreach (var w in _employee.Weaknesses) WeaknessRows.Add(w);
        }

        private void ExportPdf()
        {
            var doc = new Documents.PerformanceReportDocument(BuildModel());
            string path = SaveDialog("PDF (*.pdf)|*.pdf", ".pdf");
            if (path == null) return;
            try { QuestPDF.Fluent.Document.Create(doc.Compose).GeneratePdf(path); Dialogs.Info(L.T("Perf_Exported")); }
            catch (System.Exception ex) { Dialogs.Error(ex.Message); }
        }

        private void ExportCsv()
        {
            string path = SaveDialog("CSV (*.csv)|*.csv", ".csv");
            if (path == null) return;
            try { System.IO.File.WriteAllText(path, BuildCsv(), new System.Text.UTF8Encoding(true)); Dialogs.Info(L.T("Perf_Exported")); }
            catch (System.Exception ex) { Dialogs.Error(ex.Message); }
        }

        private Documents.PerformanceReportModel BuildModel()
        {
            var rows = new List<string[]>();
            foreach (var r in TopRows) rows.Add(new[] { r.Rank.ToString(), r.EmployeeName, r.Department ?? string.Empty, r.ScoreText, r.BandLabel });
            return new Documents.PerformanceReportModel
            {
                Title = Headline,
                Subtitle = Subline,
                CompanyName = _services.CompanyContext.Active == null ? string.Empty : _services.CompanyContext.Active.NameFr,
                AverageText = CompanyAvgText,
                BestText = BestText,
                Columns = new[] { "#", L.T("Perf_Col_Employee"), L.T("Perf_Col_Dept"), L.T("Perf_Score"), L.T("Perf_Rating") },
                Rows = rows
            };
        }

        private string BuildCsv()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Headline);
            sb.AppendLine("#;" + L.T("Perf_Col_Employee") + ";" + L.T("Perf_Col_Dept") + ";" + L.T("Perf_Score") + ";" + L.T("Perf_Rating"));
            foreach (var r in TopRows)
                sb.AppendLine(r.Rank + ";" + r.EmployeeName + ";" + (r.Department ?? "") + ";" + r.ScoreText + ";" + r.BandLabel);
            return sb.ToString();
        }

        private static string SaveDialog(string filter, string ext)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = filter, DefaultExt = ext, FileName = "rapport-evaluation" + ext };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }

    public sealed class EmployeePick
    {
        public EmployeePick(long id, string name) { Id = id; Name = name; }
        public long Id { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    // ======================================================================
    //  TAB 0 — OVERVIEW (the control centre)
    // ======================================================================
    public sealed class OverviewTabViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly OptiPaie.Desktop.ViewModels.PerformanceViewModel _root;
        private long _companyId, _activePeriodId;
        private bool _hasData, _hasBest, _hasActivePeriod;
        private string _avg = "—", _bandLabel = string.Empty, _bandKind = "neutral", _best = string.Empty, _activeName = string.Empty;
        private int _pending, _notEvaluated, _evaluated, _employeeCount;

        public OverviewTabViewModel(AppServices services, OptiPaie.Desktop.ViewModels.PerformanceViewModel root)
        {
            _services = services; _root = root;
            LogBehaviorCommand = new RelayCommand(LogBehavior);
            StartEvaluationCommand = new RelayCommand(StartEvaluation, () => _hasActivePeriod);
            HandleAlertCommand = new RelayCommand(o => HandleAlert(o as AlertRow));
            NewPeriodCommand = new RelayCommand(NewPeriod);
            RefreshCommand = new RelayCommand(Refresh);
        }

        public ObservableCollection<MoverItem> Improved { get; } = new ObservableCollection<MoverItem>();
        public ObservableCollection<MoverItem> Declined { get; } = new ObservableCollection<MoverItem>();
        public ObservableCollection<RankItem> NeedSupport { get; } = new ObservableCollection<RankItem>();
        public ObservableCollection<AlertRow> Alerts { get; } = new ObservableCollection<AlertRow>();
        public ObservableCollection<BehaviorItem> Activity { get; } = new ObservableCollection<BehaviorItem>();

        public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
        public string CompanyAvgText { get => _avg; private set => Set(ref _avg, value); }
        public string CompanyBandLabel { get => _bandLabel; private set => Set(ref _bandLabel, value); }
        public string CompanyBandKind { get => _bandKind; private set => Set(ref _bandKind, value); }
        public bool HasBest { get => _hasBest; private set => Set(ref _hasBest, value); }
        public string BestText { get => _best; private set => Set(ref _best, value); }
        public int PendingCount { get => _pending; private set => Set(ref _pending, value); }
        public int NotEvaluatedCount { get => _notEvaluated; private set => Set(ref _notEvaluated, value); }
        public int EvaluatedCount { get => _evaluated; private set => Set(ref _evaluated, value); }
        public int EmployeeCount { get => _employeeCount; private set => Set(ref _employeeCount, value); }
        public bool HasActivePeriod { get => _hasActivePeriod; private set => Set(ref _hasActivePeriod, value); }
        public string ActivePeriodName { get => _activeName; private set => Set(ref _activeName, value); }
        public bool HasAlerts => Alerts.Count > 0;
        public bool HasImproved => Improved.Count > 0;
        public bool HasDeclined => Declined.Count > 0;
        public bool HasNeedSupport => NeedSupport.Count > 0;
        public bool HasActivity => Activity.Count > 0;
        public bool AllClear => HasData && Alerts.Count == 0;

        public ICommand LogBehaviorCommand { get; }
        public ICommand StartEvaluationCommand { get; }
        public ICommand HandleAlertCommand { get; }
        public ICommand NewPeriodCommand { get; }
        public ICommand RefreshCommand { get; }

        public void SetCompany(long companyId) { _companyId = companyId; Refresh(); }

        private void Refresh()
        {
            OverviewData d = _services.Performance.GetOverview(_companyId);
            HasData = d.HasData;
            CompanyAvgText = d.HasData ? d.CompanyAverage.ToString("0.#", L.Fr) + " / 100" : "—";
            CompanyBandLabel = d.HasData ? PerfLabels.BandLabel(d.CompanyBand) : string.Empty;
            CompanyBandKind = d.HasData ? PerfLabels.BandKind(d.CompanyBand) : "neutral";
            HasBest = d.BestEmployee != null;
            BestText = d.BestEmployee == null ? string.Empty
                : string.Format(L.T("Perf_BestFormat"), d.BestEmployee.EmployeeName, d.BestEmployee.Score.ToString("0.#", L.Fr), d.BestEmployee.PeriodName);
            PendingCount = d.PendingCount;
            NotEvaluatedCount = d.NotEvaluatedCount;
            EvaluatedCount = d.EvaluatedCount;
            EmployeeCount = d.EmployeeCount;
            HasActivePeriod = d.HasActivePeriod;
            _activePeriodId = d.ActivePeriodId;
            ActivePeriodName = d.ActivePeriodName;

            Improved.Clear(); foreach (MoverRow m in d.Improved) Improved.Add(new MoverItem(m));
            Declined.Clear(); foreach (MoverRow m in d.Declined) Declined.Add(new MoverItem(m));
            NeedSupport.Clear(); foreach (EmployeeRankRow r in d.NeedSupport) NeedSupport.Add(new RankItem(r));
            Alerts.Clear(); foreach (OverviewAlert a in d.Alerts) Alerts.Add(new AlertRow(a, AlertMessage(a)));
            Activity.Clear(); foreach (BehaviorEntry b in d.RecentActivity) Activity.Add(new BehaviorItem(b));

            foreach (string p in new[] { nameof(HasAlerts), nameof(HasImproved), nameof(HasDeclined), nameof(HasNeedSupport), nameof(HasActivity), nameof(AllClear) }) Raise(p);
        }

        private static string AlertMessage(OverviewAlert a)
        {
            if (a.Kind == "decline") return string.Format(L.T("Perf_Alert_Decline"), a.EmployeeName);
            if (a.Kind == "overdue") return string.Format(L.T("Perf_Alert_Overdue"), a.PeriodName, a.Count);
            return string.Format(L.T("Perf_Alert_Pending"), a.PeriodName, a.Count);
        }

        private void LogBehavior()
        {
            var vm = new BehaviorQuickViewModel(_services, _companyId, 0);
            if (PerfDialogs.Show(new BehaviorQuickWindow { DataContext = vm }, vm)) Refresh();
        }

        private void NewPeriod()
        {
            var vm = new PeriodEditViewModel(_services, _companyId);
            if (PerfDialogs.Show(new PeriodEditWindow { DataContext = vm }, vm)) { _root.Evaluations.SetCompany(_companyId); Refresh(); }
        }

        private void StartEvaluation() { _root.GoToPeriod(_activePeriodId); }

        private void HandleAlert(AlertRow row)
        {
            if (row == null) return;
            if (row.A.Kind == "decline") _root.GoToReportEmployee(row.A.EmployeeId);
            else _root.GoToPeriod(row.A.PeriodId);
        }
    }

    public sealed class MoverItem
    {
        public MoverItem(MoverRow m) { M = m; }
        public MoverRow M { get; }
        public string EmployeeName => M.EmployeeName;
        public string ScoreText => M.Score.ToString("0.#", L.Fr);
        public string DeltaText => (M.Delta > 0 ? "+" : string.Empty) + M.Delta.ToString("0.#", L.Fr);
        public string Kind => M.Delta >= 0 ? "success" : "danger";
        public string Arrow => M.Delta >= 0 ? "▲" : "▼";
    }

    public sealed class AlertRow
    {
        public AlertRow(OverviewAlert a, string message) { A = a; Message = message; }
        public OverviewAlert A { get; }
        public string Message { get; }
        public string Kind => A.Severity;
    }

    /// <summary>A ranked employee row with a localised, semantically-coloured band.</summary>
    public sealed class RankItem
    {
        public RankItem(EmployeeRankRow r) { R = r; }
        public EmployeeRankRow R { get; }
        public int Rank => R.Rank;
        public string EmployeeName => R.EmployeeName;
        public string Department => R.Department;
        public decimal Score => R.Score;
        public string ScoreText => R.Score.ToString("0.#", L.Fr);
        public string BandLabel => PerfLabels.BandLabel(R.Band);
        public string BandKind => PerfLabels.BandKind(R.Band);
    }
}
