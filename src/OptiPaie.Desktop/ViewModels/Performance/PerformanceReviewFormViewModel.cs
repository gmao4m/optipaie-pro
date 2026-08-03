using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels.Performance
{
    /// <summary>One selectable 1-5 rating on a behavioral criterion, with its bilingual anchor.</summary>
    public sealed class ScoreChoiceViewModel : ObservableObject
    {
        private bool _isSelected;

        public ScoreChoiceViewModel(int value, string anchor, Action<int> pick)
        {
            Value = value;
            Anchor = anchor;
            PickCommand = new RelayCommand(() => pick(value));
        }

        public int Value { get; }
        public string Anchor { get; }
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
        public ICommand PickCommand { get; }
    }

    /// <summary>
    /// One criterion card in the review form. A <b>behavioral</b> criterion is rated 1-5 through
    /// a segmented control with visible anchors; a <b>KPI</b> criterion takes a target and an
    /// achieved value and shows the auto-computed score. Either way the big, colour-coded score
    /// and the anchor update live, and re-compute the review's overall through <c>changed</c>.
    /// </summary>
    public sealed class ReviewCriterionCardViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static readonly Brush Green = Frozen("#0E9F6E");
        private static readonly Brush Amber = Frozen("#E3B341");
        private static readonly Brush Red = Frozen("#C24444");
        private static readonly Brush Muted = Frozen("#8B8F99");

        private readonly Action _changed;
        private readonly Func<decimal?, decimal?, bool, decimal, decimal> _kpiScorer;
        private readonly string[] _anchors;

        private decimal _score;
        private string _comment;
        private string _targetText;
        private string _achievedText;

        public ReviewCriterionCardViewModel(PerformanceCriterion criterion, decimal scaleMax, Action changed,
            Func<decimal?, decimal?, bool, decimal, decimal> kpiScorer, string[] anchors)
        {
            _changed = changed;
            _kpiScorer = kpiScorer;
            _anchors = anchors;

            Id = criterion.Id;
            Label = criterion.Label;
            Weight = criterion.Weight;
            ScaleMax = scaleMax <= 0m ? 20m : scaleMax;
            CriterionType = criterion.CriterionType;
            HigherIsBetter = criterion.HigherIsBetter;
            _score = criterion.Score;
            _comment = criterion.Comment;
            _targetText = criterion.KpiTarget.HasValue ? criterion.KpiTarget.Value.ToString("0.##", Fr) : string.Empty;
            _achievedText = criterion.KpiAchieved.HasValue ? criterion.KpiAchieved.Value.ToString("0.##", Fr) : string.Empty;

            if (IsBehavioral && UseChoices)
            {
                for (int v = 1; v <= (int)ScaleMax; v++)
                {
                    string anchor = (_anchors != null && v <= _anchors.Length) ? _anchors[v - 1] : v.ToString();
                    Choices.Add(new ScoreChoiceViewModel(v, anchor, SetScore));
                }
                SyncChoices();
            }
        }

        public long Id { get; }
        public string Label { get; }
        public decimal Weight { get; }
        public decimal ScaleMax { get; }
        public CriterionType CriterionType { get; }
        public bool HigherIsBetter { get; }

        public bool IsKpi => CriterionType == CriterionType.Kpi;
        public bool IsBehavioral => CriterionType != CriterionType.Kpi;

        /// <summary>1-5 segmented buttons (the primary control on the new department grids).</summary>
        public bool UseChoices => IsBehavioral && ScaleMax >= 2m && ScaleMax <= 5m;

        /// <summary>Fallback slider for legacy behavioral reviews on a larger scale (e.g. /20).</summary>
        public bool UseSlider => IsBehavioral && !UseChoices;

        public ObservableCollection<ScoreChoiceViewModel> Choices { get; } = new ObservableCollection<ScoreChoiceViewModel>();

        /// <summary>Share of the overall score (set by the parent once all weights are known).</summary>
        private decimal _sharePercent;
        public decimal SharePercent
        {
            get => _sharePercent;
            set { if (Set(ref _sharePercent, value)) Raise(nameof(ShareText)); }
        }
        public string ShareText => SharePercent.ToString("0.#", Fr) + " % du total";

        public decimal Score
        {
            get => _score;
            set
            {
                decimal clamped = value < 0m ? 0m : (value > ScaleMax ? ScaleMax : value);
                if (Set(ref _score, clamped))
                {
                    Raise(nameof(ScoreText));
                    Raise(nameof(SliderValue));
                    Raise(nameof(ScoreBrush));
                    Raise(nameof(AnchorText));
                    SyncChoices();
                    _changed?.Invoke();
                }
            }
        }

        private void SetScore(int value) => Score = value;

        private void SyncChoices()
        {
            foreach (ScoreChoiceViewModel c in Choices)
            {
                c.IsSelected = c.Value == (int)Math.Round(_score, MidpointRounding.AwayFromZero);
            }
        }

        /// <summary>Double view of the score for the slider binding.</summary>
        public double SliderValue
        {
            get => (double)_score;
            set => Score = (decimal)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        public double ScaleMaxDouble => (double)ScaleMax;

        public string ScoreText => _score.ToString("0.##", Fr) + " / " + ScaleMax.ToString("0.##", Fr);

        /// <summary>The bilingual band anchor for the current score (Insuffisant … Excellent).</summary>
        public string AnchorText
        {
            get
            {
                if (_score <= 0m || _anchors == null || _anchors.Length < 5) return string.Empty;
                int band = (int)Math.Round(_score / ScaleMax * 5m, MidpointRounding.AwayFromZero);
                if (band < 1) band = 1;
                if (band > 5) band = 5;
                return _anchors[band - 1];
            }
        }

        // -- KPI inputs --------------------------------------------------------

        public string TargetText
        {
            get => _targetText;
            set { if (Set(ref _targetText, value)) ApplyKpiScore(); }
        }

        public string AchievedText
        {
            get => _achievedText;
            set { if (Set(ref _achievedText, value)) ApplyKpiScore(); }
        }

        private void ApplyKpiScore()
        {
            if (!IsKpi || _kpiScorer == null) return;
            Score = _kpiScorer(ParseNum(_targetText), ParseNum(_achievedText), HigherIsBetter, ScaleMax);
        }

        private decimal? Target => ParseNum(_targetText);
        private decimal? Achieved => ParseNum(_achievedText);

        public Brush ScoreBrush
        {
            get
            {
                if (ScaleMax <= 0m) return Muted;
                decimal pct = _score / ScaleMax * 100m;
                if (pct >= 70m) return Green;
                if (pct >= 50m) return Amber;
                return Red;
            }
        }

        public string Comment { get => _comment; set => Set(ref _comment, value); }

        public PerformanceCriterion ToEntity()
        {
            return new PerformanceCriterion
            {
                Label = Label,
                Weight = Weight,
                Score = _score,
                Comment = _comment,
                CriterionType = CriterionType,
                HigherIsBetter = HigherIsBetter,
                KpiTarget = IsKpi ? Target : null,
                KpiAchieved = IsKpi ? Achieved : null
            };
        }

        private static decimal? ParseNum(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            string cleaned = s.Replace(" ", string.Empty).Replace(",", ".");
            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v)
                ? v : (decimal?)null;
        }

        private static Brush Frozen(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }
    }

    /// <summary>
    /// The signature review form: scores each criterion on its scale with a live, colour-coded
    /// overall, captures observations and an optional self-assessment, and finalises the
    /// review. Numbers and scoring come straight from the Performance service — the form only
    /// presents and saves them.
    /// </summary>
    public sealed class PerformanceReviewFormViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static readonly Brush Green = Freeze("#0E9F6E");
        private static readonly Brush Amber = Freeze("#E3B341");
        private static readonly Brush Red = Freeze("#C24444");

        private readonly AppServices _services;
        private readonly long _reviewId;

        private Employee _employee;
        private Company _company;
        private PerformanceReview _review;

        private string _reviewer;
        private string _comments;
        private string _selfComments;
        private string _selfScoreText;
        private string _overallText = "0 / 20";
        private string _overallPercentText = string.Empty;
        private string _ratingText = string.Empty;
        private Brush _overallBrush = Amber;
        private string _attendanceText = string.Empty;
        private bool _isCompleted;
        private bool _showOutOf20;
        private decimal _overall;
        private string _suggestionText = string.Empty;
        private readonly List<string> _trainingTitles = new List<string>();
        private string[] _anchors;

        public PerformanceReviewFormViewModel(AppServices services, long reviewId)
        {
            _services = services;
            _reviewId = reviewId;

            SaveCommand = new RelayCommand(() => SaveInternal(false));
            CompleteCommand = new RelayCommand(() => SaveInternal(true), () => IsEditable);
            ReopenCommand = new RelayCommand(Reopen, () => _isCompleted);
            PdfCommand = new RelayCommand(ExportPdf);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            Load();
        }

        public Action<bool> RequestClose { get; set; }

        public string Title { get; private set; } = "Évaluation";
        public string EmployeeName { get; private set; }
        public string EmployeeMeta { get; private set; }
        public string PeriodLabel { get; private set; }
        public decimal ScaleMax { get; private set; } = 20m;

        public ObservableCollection<ReviewCriterionCardViewModel> Criteria { get; } =
            new ObservableCollection<ReviewCriterionCardViewModel>();

        public string Reviewer { get => _reviewer; set => Set(ref _reviewer, value); }
        public string Comments { get => _comments; set => Set(ref _comments, value); }
        public string SelfComments { get => _selfComments; set => Set(ref _selfComments, value); }
        public string SelfScoreText { get => _selfScoreText; set => Set(ref _selfScoreText, value); }

        public string OverallText { get => _overallText; private set => Set(ref _overallText, value); }
        public string OverallPercentText { get => _overallPercentText; private set => Set(ref _overallPercentText, value); }
        public string RatingText { get => _ratingText; private set => Set(ref _ratingText, value); }
        public Brush OverallBrush { get => _overallBrush; private set => Set(ref _overallBrush, value); }

        /// <summary>Toggle: show the score out of 20 instead of its native scale (typically /5).</summary>
        public bool ShowOutOf20
        {
            get => _showOutOf20;
            set { if (Set(ref _showOutOf20, value)) RefreshOverallDisplay(); }
        }

        public string AttendanceText { get => _attendanceText; private set => Set(ref _attendanceText, value); }
        public bool HasAttendance => !string.IsNullOrEmpty(_attendanceText);

        /// <summary>Training suggestion for the weakest criterion (Performance → Training integration).</summary>
        public string SuggestionText { get => _suggestionText; private set { if (Set(ref _suggestionText, value)) Raise(nameof(HasSuggestion)); } }
        public bool HasSuggestion => !string.IsNullOrEmpty(_suggestionText);

        public bool IsCompleted { get => _isCompleted; private set { if (Set(ref _isCompleted, value)) { Raise(nameof(IsEditable)); } } }
        public bool IsEditable => !_isCompleted;

        public ICommand SaveCommand { get; }
        public ICommand CompleteCommand { get; }
        public ICommand ReopenCommand { get; }
        public ICommand PdfCommand { get; }
        public ICommand CancelCommand { get; }

        private void Load()
        {
            PerformanceDetail detail = _services.Performance.GetDetail(_reviewId);
            if (detail == null)
            {
                return;
            }

            _review = detail.Review;
            ScaleMax = _review.ScaleMax <= 0m ? 20m : _review.ScaleMax;
            _employee = _services.Employees.Get(_review.EmployeeId);
            _company = _employee != null ? _services.Companies.Get(_employee.CompanyId) : null;

            EmployeeName = _employee != null ? (_employee.LastNameFr + " " + _employee.FirstNameFr).Trim() : "#" + _review.EmployeeId;
            EmployeeMeta = _employee != null
                ? string.Join("  •  ", new[] { _employee.Poste, _employee.Department }.Where(s => !string.IsNullOrWhiteSpace(s)))
                : string.Empty;
            PeriodLabel = _review.PeriodLabel;
            _reviewer = _review.Reviewer;
            _comments = _review.Comments;
            _selfComments = _review.SelfComments;
            _selfScoreText = _review.SelfScore.HasValue ? _review.SelfScore.Value.ToString("0.##", Fr) : string.Empty;
            IsCompleted = _review.Status == PerformanceStatus.Completed;
            Title = IsCompleted ? "Évaluation finalisée" : "Évaluation";

            _anchors = new[]
            {
                L("Eval_Anchor1"), L("Eval_Anchor2"), L("Eval_Anchor3"), L("Eval_Anchor4"), L("Eval_Anchor5")
            };

            foreach (PerformanceCriterion c in detail.Criteria)
            {
                Criteria.Add(new ReviewCriterionCardViewModel(c, ScaleMax, Recompute, _services.Performance.ScoreKpi, _anchors));
            }

            if (detail.Attendance != null)
            {
                AttendanceContext a = detail.Attendance;
                AttendanceText = a.AbsentDays + " absence(s) · " + a.LateCount + " retard(s) · " +
                                 a.OvertimeHours.ToString("0.##", Fr) + " h sup.";
            }

            if (_employee != null)
            {
                foreach (TrainingSummary t in _services.Training.GetByCompany(_employee.CompanyId))
                {
                    if (!string.IsNullOrWhiteSpace(t.Title)) _trainingTitles.Add(t.Title);
                }
            }

            Recompute();
            RaiseAll();
        }

        private void RaiseAll()
        {
            Raise(nameof(Title));
            Raise(nameof(EmployeeName));
            Raise(nameof(EmployeeMeta));
            Raise(nameof(PeriodLabel));
            Raise(nameof(ScaleMax));
            Raise(nameof(Reviewer));
            Raise(nameof(Comments));
            Raise(nameof(SelfComments));
            Raise(nameof(SelfScoreText));
            Raise(nameof(HasAttendance));
        }

        private void Recompute()
        {
            decimal totalWeight = Criteria.Sum(c => c.Weight);
            _overall = totalWeight > 0m
                ? Math.Round(Criteria.Sum(c => c.Score * c.Weight) / totalWeight, 2, MidpointRounding.AwayFromZero)
                : 0m;

            foreach (ReviewCriterionCardViewModel c in Criteria)
            {
                c.SharePercent = totalWeight > 0m ? Math.Round(c.Weight / totalWeight * 100m, 1, MidpointRounding.AwayFromZero) : 0m;
            }

            RefreshOverallDisplay();
            UpdateSuggestion();
        }

        /// <summary>Formats the header score in the chosen scale, plus percent and rating band.</summary>
        private void RefreshOverallDisplay()
        {
            decimal pct = ScaleMax > 0m ? _overall / ScaleMax * 100m : 0m;

            if (_showOutOf20)
            {
                decimal on20 = ScaleMax > 0m ? Math.Round(_overall / ScaleMax * 20m, 2, MidpointRounding.AwayFromZero) : 0m;
                OverallText = on20.ToString("0.##", Fr) + " / 20";
            }
            else
            {
                OverallText = _overall.ToString("0.##", Fr) + " / " + ScaleMax.ToString("0.##", Fr);
            }

            OverallPercentText = pct.ToString("0.#", Fr) + " %";
            RatingText = _services.Performance.RateScaled(_overall, ScaleMax);
            OverallBrush = pct >= 70m ? Green : (pct >= 50m ? Amber : Red);
        }

        /// <summary>Localized string lookup for VM-side text (bilingual anchors).</summary>
        private string L(string key) => _services.Localization != null ? _services.Localization.GetString(key) : key;

        /// <summary>
        /// Performance → Training: if a criterion scores below half its scale, suggest a matching
        /// company training course (or a generic suggestion). Read-only — enrols no one.
        /// </summary>
        private void UpdateSuggestion()
        {
            ReviewCriterionCardViewModel weakest = null;
            decimal weakestPct = 100m;
            foreach (ReviewCriterionCardViewModel c in Criteria)
            {
                decimal p = c.ScaleMax > 0m ? c.Score / c.ScaleMax * 100m : 0m;
                if (p < weakestPct) { weakestPct = p; weakest = c; }
            }

            if (weakest == null || weakestPct >= 50m)
            {
                SuggestionText = string.Empty;
                return;
            }

            string match = null;
            foreach (string title in _trainingTitles)
            {
                if (Matches(weakest.Label, title)) { match = title; break; }
            }

            SuggestionText = match != null
                ? "Point faible : « " + weakest.Label + " ». Formation suggérée : « " + match + " »."
                : "Point faible : « " + weakest.Label + " ». Envisagez une formation sur ce thème.";
        }

        /// <summary>True when a significant word (≥4 chars) of the criterion label appears in the text.</summary>
        private static bool Matches(string label, string text)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(text)) return false;
            string t = text.ToLowerInvariant();
            foreach (string word in label.ToLowerInvariant().Split(new[] { ' ', '\'', '/', '-', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length >= 4 && t.Contains(word)) return true;
            }
            return false;
        }

        private void SaveInternal(bool finalise)
        {
            if (_review == null)
            {
                Dialogs.Error("Évaluation introuvable.");
                return;
            }

            if (_isCompleted)
            {
                Dialogs.Info("Cette évaluation est finalisée. Rouvrez-la pour la modifier.");
                return;
            }

            decimal? self = null;
            if (!string.IsNullOrWhiteSpace(_selfScoreText) &&
                decimal.TryParse(_selfScoreText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
            {
                self = parsed;
            }

            var review = new PerformanceReview
            {
                Id = _review.Id,
                EmployeeId = _review.EmployeeId,
                PeriodYear = _review.PeriodYear,
                PeriodLabel = _review.PeriodLabel,
                ReviewDate = _review.ReviewDate == default(DateTime) ? DateTime.Today : _review.ReviewDate,
                Reviewer = _reviewer,
                Comments = _comments,
                ScaleMax = ScaleMax,
                SelfScore = self,
                SelfComments = _selfComments
            };

            Result saved = _services.Performance.Save(review, Criteria.Select(c => c.ToEntity()).ToList());
            if (saved.IsFailure)
            {
                Dialogs.Error(saved.Error);
                return;
            }

            if (finalise)
            {
                Result done = _services.Performance.Complete(_review.Id);
                if (done.IsFailure)
                {
                    Dialogs.Error(done.Error);
                    return;
                }
            }

            RequestClose?.Invoke(true);
        }

        private void Reopen()
        {
            Result r = _services.Performance.Reopen(_reviewId);
            if (r.IsFailure) { Dialogs.Error(r.Error); return; }
            IsCompleted = false;
            Title = "Évaluation";
            Raise(nameof(Title));
        }

        private void ExportPdf()
        {
            PerformanceDetail detail = _services.Performance.GetDetail(_reviewId);
            if (detail == null || _employee == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = "Evaluation_" + (_employee.LastNameFr ?? "employe") + "_" + _review.PeriodYear + ".pdf"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var document = new PerformanceReviewDocument(new PerformanceReviewModel
                {
                    Company = _company,
                    Employee = _employee,
                    Detail = detail
                });
                Document.Create(document.Compose).GeneratePdf(dialog.FileName);
                try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch (Exception ex) { _services.Logger.Warn("Ouverture PDF impossible : " + ex.Message); }
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export PDF évaluation", ex);
                Dialogs.Error("Impossible de générer le PDF : " + ex.Message);
            }
        }

        private static Brush Freeze(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }
    }
}
