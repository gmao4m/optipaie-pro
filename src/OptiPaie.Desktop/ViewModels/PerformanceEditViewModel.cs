using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One editable criterion row. The score is a /20 value.</summary>
    public sealed class CriterionRowViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _label;
        private decimal _weight;
        private decimal _score;
        private string _comment;

        public CriterionRowViewModel(Action changed, PerformanceCriterion criterion = null)
        {
            _changed = changed;
            if (criterion != null)
            {
                _label = criterion.Label;
                _weight = criterion.Weight;
                _score = criterion.Score;
                _comment = criterion.Comment;
            }
            else
            {
                _weight = 1m;
            }
        }

        public string Label { get => _label; set => Set(ref _label, value); }

        public decimal Weight
        {
            get => _weight;
            set { if (Set(ref _weight, value)) _changed?.Invoke(); }
        }

        public decimal Score
        {
            get => _score;
            set { if (Set(ref _score, value)) _changed?.Invoke(); }
        }

        public string Comment { get => _comment; set => Set(ref _comment, value); }

        public PerformanceCriterion ToEntity()
        {
            return new PerformanceCriterion
            {
                Label = Label,
                Weight = Weight,
                Score = Score,
                Comment = Comment
            };
        }
    }

    /// <summary>
    /// Creates or edits a performance review: the header, the weighted /20 criteria (with
    /// a live overall score) and — read-only — the attendance context pulled from the
    /// Attendance module.
    /// </summary>
    public sealed class PerformanceEditViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private long _reviewId;
        private readonly int _year;

        private Employee _selectedEmployee;
        private string _periodLabel;
        private DateTime _reviewDate;
        private string _reviewer;
        private string _comments;
        private string _overallText = "0 / 20";
        private string _ratingText = string.Empty;
        private string _attendanceText = string.Empty;
        private bool _isCompleted;

        public PerformanceEditViewModel(AppServices services, IReadOnlyList<Employee> employees, int year, long reviewId)
        {
            _services = services;
            _year = year;
            _reviewId = reviewId;

            foreach (Employee e in employees) Employees.Add(e);

            SaveCommand = new RelayCommand(Save);
            AddCriterionCommand = new RelayCommand(AddCriterion);
            RemoveCriterionCommand = new RelayCommand(p => RemoveCriterion(p as CriterionRowViewModel));
            PdfCommand = new RelayCommand(ExportPdf, () => _reviewId > 0);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            if (_reviewId > 0)
            {
                LoadExisting();
            }
            else
            {
                _selectedEmployee = Employees.Count > 0 ? Employees[0] : null;
                // Auto-default to the CURRENT period (month + year) so a monthly cadence needs
                // no manual date entry; the user stays free to type any past period instead.
                _periodLabel = DefaultCurrentPeriod(year);
                _reviewDate = DateTime.Today;
                Title = "Nouvelle évaluation";
                foreach (string label in new[] { "Qualité du travail", "Productivité / rendement", "Assiduité et ponctualité", "Travail d'équipe", "Initiative et autonomie" })
                {
                    Criteria.Add(new CriterionRowViewModel(Recompute) { Label = label });
                }

                Recompute();
            }
        }

        public Action<bool> RequestClose { get; set; }

        public string Title { get; private set; }

        public ObservableCollection<Employee> Employees { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<CriterionRowViewModel> Criteria { get; } = new ObservableCollection<CriterionRowViewModel>();

        public bool CanChooseEmployee => _reviewId == 0;

        public Employee SelectedEmployee
        {
            get => _selectedEmployee;
            set => Set(ref _selectedEmployee, value);
        }

        public string PeriodLabel { get => _periodLabel; set => Set(ref _periodLabel, value); }
        public DateTime ReviewDate { get => _reviewDate; set => Set(ref _reviewDate, value); }
        public string Reviewer { get => _reviewer; set => Set(ref _reviewer, value); }
        public string Comments { get => _comments; set => Set(ref _comments, value); }

        public string OverallText { get => _overallText; private set => Set(ref _overallText, value); }
        public string RatingText { get => _ratingText; private set => Set(ref _ratingText, value); }
        public string AttendanceText { get => _attendanceText; private set => Set(ref _attendanceText, value); }
        public bool HasAttendance => !string.IsNullOrEmpty(_attendanceText);

        /// <summary>A finalised review is read-only until it is reopened from the list.</summary>
        public bool IsCompleted { get => _isCompleted; private set => Set(ref _isCompleted, value); }
        public bool IsEditable => !_isCompleted;

        public ICommand SaveCommand { get; }
        public ICommand AddCriterionCommand { get; }
        public ICommand RemoveCriterionCommand { get; }
        public ICommand PdfCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>
        /// The current-period label for a new review: the current month + the evaluation year
        /// (e.g. "Juillet 2026"), or just the year when evaluating a different year than today's.
        /// </summary>
        private static string DefaultCurrentPeriod(int year)
        {
            if (year != DateTime.Today.Year)
            {
                return year.ToString(CultureInfo.InvariantCulture);
            }

            string month = Fr.DateTimeFormat.GetMonthName(DateTime.Today.Month);
            if (month.Length > 0) month = char.ToUpper(month[0], Fr) + month.Substring(1);
            return month + " " + year.ToString(CultureInfo.InvariantCulture);
        }

        private void LoadExisting()
        {
            PerformanceDetail detail = _services.Performance.GetDetail(_reviewId);
            if (detail == null)
            {
                Title = "Évaluation";
                return;
            }

            PerformanceReview review = detail.Review;
            _selectedEmployee = Employees.FirstOrDefault(e => e.Id == review.EmployeeId);
            _periodLabel = review.PeriodLabel;
            _reviewDate = review.ReviewDate;
            _reviewer = review.Reviewer;
            _comments = review.Comments;
            IsCompleted = review.Status == Core.Enums.PerformanceStatus.Completed;
            Title = IsCompleted ? "Évaluation finalisée" : "Modifier l'évaluation";

            foreach (PerformanceCriterion c in detail.Criteria)
            {
                Criteria.Add(new CriterionRowViewModel(Recompute, c));
            }

            SetAttendance(detail.Attendance);
            Recompute();
        }

        private void SetAttendance(AttendanceContext ctx)
        {
            if (ctx == null)
            {
                AttendanceText = string.Empty;
            }
            else
            {
                AttendanceText = "Présence " + _year + " : " + ctx.AbsentDays + " absence(s) · " +
                                 ctx.LateCount + " retard(s) · " +
                                 ctx.OvertimeHours.ToString("0.##", Fr) + " h supplémentaires";
            }

            Raise(nameof(HasAttendance));
        }

        private void AddCriterion()
        {
            Criteria.Add(new CriterionRowViewModel(Recompute) { Label = "Nouveau critère" });
            Recompute();
        }

        private void RemoveCriterion(CriterionRowViewModel row)
        {
            if (row == null) return;
            Criteria.Remove(row);
            Recompute();
        }

        private void Recompute()
        {
            decimal totalWeight = Criteria.Sum(c => c.Weight);
            decimal overall = totalWeight > 0m
                ? Math.Round(Criteria.Sum(c => c.Score * c.Weight) / totalWeight, 2, MidpointRounding.AwayFromZero)
                : 0m;

            OverallText = overall.ToString("0.##", Fr) + " / 20";
            RatingText = _services.Performance.Rate(overall);
        }

        private void Save()
        {
            if (IsCompleted)
            {
                Dialogs.Info("Cette évaluation est finalisée. Rouvrez-la pour la modifier.");
                return;
            }

            if (_selectedEmployee == null)
            {
                Dialogs.Error("Sélectionnez un employé.");
                return;
            }

            // Create the draft on first save so the id exists for later edits and PDF.
            if (_reviewId == 0)
            {
                Result<long> created = _services.Performance.CreateDraft(
                    _selectedEmployee.Id, _year, _periodLabel, _reviewer);
                if (created.IsFailure)
                {
                    Dialogs.Error(created.Error);
                    return;
                }

                _reviewId = created.Value;
            }

            var review = new PerformanceReview
            {
                Id = _reviewId,
                EmployeeId = _selectedEmployee.Id,
                PeriodYear = _year,
                PeriodLabel = _periodLabel,
                ReviewDate = _reviewDate,
                Reviewer = _reviewer,
                Comments = _comments
            };

            Result saved = _services.Performance.Save(review, Criteria.Select(c => c.ToEntity()).ToList());
            if (saved.IsFailure)
            {
                Dialogs.Error(saved.Error);
                return;
            }

            // Refresh the attendance context now that the review is persisted.
            SetAttendance(_services.Performance.GetDetail(_reviewId)?.Attendance);
            RequestClose?.Invoke(true);
        }

        private void ExportPdf()
        {
            PerformanceDetail detail = _services.Performance.GetDetail(_reviewId);
            if (detail == null || _selectedEmployee == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = "Evaluation_" + _year + ".pdf"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                Company company = _services.Companies.Get(_selectedEmployee.CompanyId);
                var document = new PerformanceReviewDocument(new PerformanceReviewModel
                {
                    Company = company,
                    Employee = _selectedEmployee,
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
    }
}
