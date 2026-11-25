using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Desktop.Localization;
using OptiPaie.Services;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One line of the demo-removal preview: what is deleted and how many rows.</summary>
    public sealed class DemoRemovalRow
    {
        public DemoRemovalRow(string label, long count)
        {
            Label = label;
            Count = count;
        }

        public string Label { get; }
        public long Count { get; }
        public string CountText => Count.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));
    }

    /// <summary>
    /// What the preview window shows before the trial demo company is removed: the company, how it was
    /// recognised, every non-empty table with its row count, the total, and — when the demo is the only
    /// company — the refusal (the Delete button is then disabled).
    /// </summary>
    public sealed class DemoRemovalPreviewViewModel
    {
        // Table → (French, Arabic) label. Every table of the deletion plan has one.
        private static readonly Dictionary<string, (string Fr, string Ar)> Labels = new Dictionary<string, (string, string)>
        {
            ["Employees"] = ("Employés", "الموظّفون"),
            ["Payslips"] = ("Bulletins de paie", "كشوف الرواتب"),
            ["PayrollDetails"] = ("Lignes des bulletins", "أسطر كشوف الرواتب"),
            ["PayrollRuns"] = ("Campagnes de paie", "دورات الأجور"),
            ["ArchiveDocuments"] = ("Documents archivés", "الوثائق المؤرشفة"),
            ["Loans"] = ("Prêts", "القروض"),
            ["LoanRepayments"] = ("Remboursements de prêts", "تسديدات القروض"),
            ["AttendanceRecords"] = ("Pointages", "سجلّات الحضور"),
            ["LeaveRequests"] = ("Congés", "طلبات العطل"),
            ["EmploymentContracts"] = ("Contrats", "العقود"),
            ["WorkCertificates"] = ("Attestations de travail", "شهادات العمل"),
            ["EmployeeElements"] = ("Éléments de paie des employés", "عناصر أجور الموظّفين"),
            ["BehaviorLogs"] = ("Suivi disciplinaire", "سجلّ الانضباط"),
            ["Departments"] = ("Départements", "الأقسام"),
            ["EvaluationScores"] = ("Notes d'évaluation", "درجات التقييم"),
            ["Evaluations"] = ("Évaluations", "التقييمات"),
            ["EvalCriteria"] = ("Critères d'évaluation", "معايير التقييم"),
            ["EvalTemplates"] = ("Modèles d'évaluation", "نماذج التقييم"),
            ["EvalPeriods"] = ("Campagnes d'évaluation", "فترات التقييم"),
            ["Interviews"] = ("Entretiens de recrutement", "مقابلات التوظيف"),
            ["CandidateAttachments"] = ("Pièces des candidats", "مرفقات المترشّحين"),
            ["Candidates"] = ("Candidats", "المترشّحون"),
            ["JobPostings"] = ("Offres d'emploi", "عروض العمل"),
            ["TrainingParticipants"] = ("Participants aux formations", "المشاركون في التكوين"),
            ["TrainingSessions"] = ("Sessions de formation", "دورات التكوين"),
            ["AssetAssignments"] = ("Affectations de matériel", "إسناد العتاد"),
            ["Assets"] = ("Matériel", "العتاد"),
            ["Holidays"] = ("Jours fériés propres à la société", "العطل الخاصّة بالشركة"),
            ["LeaveTypes"] = ("Types de congé propres à la société", "أنواع العطل الخاصّة بالشركة"),
            ["CompanyWorkCalendar"] = ("Calendrier de travail", "رزنامة العمل"),
            ["AuditLog"] = ("Journal d'audit de la société", "سجلّ التدقيق الخاصّ بالشركة"),
            ["Companies"] = ("La société de démonstration", "شركة العرض التجريبي"),
            ["Candidates.HiredEmployeeId"] = ("Liens d'embauche détachés (autres sociétés)", "روابط توظيف مفصولة (شركات أخرى)"),
        };

        // Most meaningful first; anything else follows in plan order.
        private static readonly string[] Order =
        {
            "Companies", "Employees", "Payslips", "PayrollDetails", "PayrollRuns", "AttendanceRecords", "LeaveRequests",
            "EmploymentContracts", "Loans", "LoanRepayments", "Departments"
        };

        public DemoRemovalPreviewViewModel(DemoDeletionPreview preview, bool arabic)
        {
            TranslationSource t = TranslationSource.Instance;
            CompanyName = preview.Company.Name;
            MatchedText = preview.Company.MatchedBy == "flag" ? t["Demo_Preview_MatchedFlag"] : t["Demo_Preview_MatchedFingerprint"];

            List<TableRows> nonEmpty = preview.Rows.Where(r => r.Rows > 0).ToList();
            Rows = nonEmpty
                .OrderBy(r => { int i = System.Array.IndexOf(Order, r.Table); return i < 0 ? int.MaxValue : i; })
                .Select(r => new DemoRemovalRow(LabelOf(r.Table, arabic), r.Rows))
                .ToList();

            TotalRows = preview.TotalRows;
            TotalText = string.Format(CultureInfo.InvariantCulture, t["Demo_Preview_Total"],
                TotalRows.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")));
            IsOnlyCompany = preview.IsOnlyCompany;
        }

        public string CompanyName { get; }
        public string MatchedText { get; }
        public IReadOnlyList<DemoRemovalRow> Rows { get; }
        public long TotalRows { get; }
        public string TotalText { get; }
        public bool IsOnlyCompany { get; }
        public bool CanDelete => !IsOnlyCompany;

        public static string LabelOf(string table, bool arabic) =>
            Labels.TryGetValue(table, out var l) ? (arabic ? l.Ar : l.Fr) : table;
    }
}
