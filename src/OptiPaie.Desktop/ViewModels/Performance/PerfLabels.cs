using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Localization;

namespace OptiPaie.Desktop.ViewModels.Performance
{
    /// <summary>
    /// Localised labels and status-pill "kinds" for the Performance (Évaluation) enums.
    /// Central so the hub, the evaluation screen, the reports and the 360° profile all read
    /// the same wording. Kinds map to the shared pill buckets: success / accent / pending / danger.
    /// </summary>
    public static class PerfLabels
    {
        private static string L(string key) => TranslationSource.Instance[key];

        public static string BandLabel(ClassificationBand b) => L("Enum_Band_" + b);

        public static string BandKind(ClassificationBand b)
        {
            switch (b)
            {
                // Three clear semantic colours: green = good, amber = medium, red = weak.
                case ClassificationBand.Excellent:
                case ClassificationBand.VeryGood:
                case ClassificationBand.Good: return "success";
                case ClassificationBand.Average: return "pending";
                default: return "danger";
            }
        }

        public static string CategoryLabel(CriterionCategory c) => L("Enum_Category_" + c);

        public static string ScoreTypeLabel(ScoreType s) => L("Enum_ScoreType_" + s);

        public static string CadenceLabel(PeriodCadence c) => L("Enum_Cadence_" + c);

        public static string PeriodStatusLabel(PeriodStatus s) => L("Enum_PeriodStatus_" + s);

        public static string PeriodStatusKind(PeriodStatus s) => s == PeriodStatus.Open ? "accent" : "neutral";

        public static string EvalStatusLabel(EvaluationStatus s) => L("Enum_EvalStatus_" + s);

        public static string EvalStatusKind(EvaluationStatus s) => s == EvaluationStatus.Done ? "success" : "pending";

        public static string WeightingLabel(WeightingMode m) => L("Enum_Weighting_" + m);

        public static string RecommendationLabel(string key) => string.IsNullOrEmpty(key) ? string.Empty : L(key);
    }
}
