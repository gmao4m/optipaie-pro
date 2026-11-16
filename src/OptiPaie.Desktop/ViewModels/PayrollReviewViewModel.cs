using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The pre-run review (Commit 4): payroll shows the truth before it runs — one row per employee
    /// with days due / worked / absent / leave / half / deduction, the basis + divisor used, and a
    /// plain flag for anyone with no attendance. The user must confirm before any payslip is issued;
    /// the confirmation is audited by the caller.
    /// </summary>
    public sealed class PayrollReviewViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public PayrollReviewViewModel(string title, IEnumerable<PayrollReviewRow> rows)
        {
            Title = title ?? "Revue avant émission  ·  مراجعة قبل الإصدار";
            foreach (PayrollReviewRow r in rows ?? Enumerable.Empty<PayrollReviewRow>())
            {
                Rows.Add(new ReviewRowVM(r));
            }
            NoDataCount = Rows.Count(r => r.NoAttendance);
            ConfirmCommand = new RelayCommand(() => RequestClose?.Invoke(true));
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public string Title { get; }
        public ObservableCollection<ReviewRowVM> Rows { get; } = new ObservableCollection<ReviewRowVM>();
        public int NoDataCount { get; }
        public bool HasNoData => NoDataCount > 0;
        public string NoDataNote => NoDataCount == 0 ? string.Empty :
            NoDataCount + " employé(s) sans pointage → mois complet payé.  ·  " + NoDataCount + " موظّف(ون) دون تسجيل → يُدفع الشهر كاملاً.";

        /// <summary>Set by the host window: true = confirmed (issue), false = cancelled.</summary>
        public Action<bool> RequestClose { get; set; }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public sealed class ReviewRowVM
        {
            public ReviewRowVM(PayrollReviewRow r)
            {
                Employee = r.Employee;
                DaysDue = Num(r.DaysDue);
                Worked = Num(r.Worked);
                Absent = Num(r.Absent);
                Leave = Num(r.Leave);
                Half = Num(r.Half);
                Deduction = r.Deduction.ToString("0.##", Fr) + " DA";
                Basis = BasisLabel(r.Basis) + " (÷" + Num(r.Divisor) + ")";
                NoAttendance = r.NoAttendance;
                Flag = r.NoAttendance ? "aucun pointage · لا تسجيل" : string.Empty;
            }

            public string Employee { get; }
            public string DaysDue { get; }
            public string Worked { get; }
            public string Absent { get; }
            public string Leave { get; }
            public string Half { get; }
            public string Deduction { get; }
            public string Basis { get; }
            public bool NoAttendance { get; }
            public string Flag { get; }

            private static string Num(decimal v) => v.ToString("0.##", Fr);

            private static string BasisLabel(ProrationBasis b)
            {
                switch (b)
                {
                    case ProrationBasis.FixedThirtieths: return "Trentièmes";
                    case ProrationBasis.WorkingDays: return "Jours ouvrables";
                    default: return "Jours du mois";
                }
            }
        }
    }
}
