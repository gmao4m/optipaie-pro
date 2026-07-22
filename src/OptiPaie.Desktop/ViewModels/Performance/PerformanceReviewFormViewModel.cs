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
    /// <summary>
    /// One criterion card in the review form: a big, colour-coded score set with a slider
    /// (0..scale), the criterion's share of the overall score, and a comment area. Raising
    /// the score re-computes the review's live overall through <paramref name="changed"/>.
    /// </summary>
    public sealed class ReviewCriterionCardViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static readonly Brush Green = Frozen("#2E9E6C");
        private static readonly Brush Amber = Frozen("#D9A441");
        private static readonly Brush Red = Frozen("#C24444");
        private static readonly Brush Muted = Frozen("#8B8F99");

        private readonly Action _changed;
        private decimal _score;
        private string _comment;

        public ReviewCriterionCardViewModel(PerformanceCriterion criterion, decimal scaleMax, Action changed)
        {
            _changed = changed;
            Id = criterion.Id;
            Label = criterion.Label;
            Weight = criterion.Weight;
            ScaleMax = scaleMax <= 0m ? 20m : scaleMax;
            _score = criterion.Score;
            _comment = criterion.Comment;
        }

        public long Id { get; }
        public string Label { get; }
        public decimal Weight { get; }
        public decimal ScaleMax { get; }

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
                    _changed?.Invoke();
                }
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
            return new PerformanceCriterion { Label = Label, Weight = Weight, Score = _score, Comment = _comment };
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
        private static readonly Brush Green = Freeze("#2E9E6C");
        private static readonly Brush Amber = Freeze("#D9A441");
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
        private string _ratingText = string.Empty;
        private Brush _overallBrush = Amber;
        private string _attendanceText = string.Empty;
        private bool _isCompleted;

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
        public string RatingText { get => _ratingText; private set => Set(ref _ratingText, value); }
        public Brush OverallBrush { get => _overallBrush; private set => Set(ref _overallBrush, value); }

        public string AttendanceText { get => _attendanceText; private set => Set(ref _attendanceText, value); }
        public bool HasAttendance => !string.IsNullOrEmpty(_attendanceText);

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

            foreach (PerformanceCriterion c in detail.Criteria)
            {
                Criteria.Add(new ReviewCriterionCardViewModel(c, ScaleMax, Recompute));
            }

            if (detail.Attendance != null)
            {
                AttendanceContext a = detail.Attendance;
                AttendanceText = a.AbsentDays + " absence(s) · " + a.LateCount + " retard(s) · " +
                                 a.OvertimeHours.ToString("0.##", Fr) + " h sup.";
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
            decimal overall = totalWeight > 0m
                ? Math.Round(Criteria.Sum(c => c.Score * c.Weight) / totalWeight, 2, MidpointRounding.AwayFromZero)
                : 0m;

            foreach (ReviewCriterionCardViewModel c in Criteria)
            {
                c.SharePercent = totalWeight > 0m ? Math.Round(c.Weight / totalWeight * 100m, 1, MidpointRounding.AwayFromZero) : 0m;
            }

            OverallText = overall.ToString("0.##", Fr) + " / " + ScaleMax.ToString("0.##", Fr);
            RatingText = _services.Performance.RateScaled(overall, ScaleMax);

            decimal pct = ScaleMax > 0m ? overall / ScaleMax * 100m : 0m;
            OverallBrush = pct >= 70m ? Green : (pct >= 50m ? Amber : Red);
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
