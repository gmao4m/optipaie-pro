using System;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// What the user is told when the licence's company scope changes (Mono ⇄ Multi) after a successful
    /// check, and what the "Actualiser la licence" button reports. Pure, so it is testable.
    /// The scope is the one the gate enforces: Multi only when the licence is usable AND carries the
    /// explicit Multi sentinel (maxCompanies = 0) — a failed/offline check never changes it.
    /// </summary>
    public static class LicenseScopeChange
    {
        public const string BecameMultiAr = "تم تحديث ترخيصك: أصبح متعدد الشركات.";
        public const string BecameMultiFr = "Votre licence a été mise à jour : elle est désormais multi-sociétés.";

        public const string BecameMonoFr =
            "Votre licence a été mise à jour : elle est désormais mono-société. Toutes vos entreprises existantes restent " +
            "accessibles et rien n'a été supprimé ; seule la création d'une nouvelle entreprise est bloquée.";
        public const string BecameMonoAr =
            "تم تحديث ترخيصك: أصبح لشركة واحدة. تبقى جميع شركاتك الحالية متاحة ولم يُحذَف أيّ شيء؛ يُمنَع فقط إنشاء شركة جديدة.";

        /// <summary>True when the snapshot entitles to several companies.</summary>
        public static bool IsMulti(LicenseSnapshot s) => s != null && s.IsUsable && s.IsMultiCompany;

        /// <summary>
        /// The message to show when the scope changed between two USABLE snapshots, or null when it did not
        /// (or when either side is not usable — a suspension is a status change, not a scope change).
        /// </summary>
        public static string Describe(LicenseSnapshot before, LicenseSnapshot after)
        {
            if (before == null || after == null || !before.IsUsable || !after.IsUsable) return null;
            bool was = IsMulti(before), now = IsMulti(after);
            if (was == now) return null;
            return now
                ? BecameMultiAr + Environment.NewLine + BecameMultiFr
                : BecameMonoAr + Environment.NewLine + BecameMonoFr;
        }

        /// <summary>The report of a manual "refresh licence" check.</summary>
        public static string RefreshReport(LicenseResult result, LicenseSnapshot before, LicenseSnapshot after)
        {
            string change = Describe(before, after);
            if (change != null) return change;

            switch (result?.Kind ?? LicenseResultKind.Error)
            {
                case LicenseResultKind.Ok:
                    string scope = IsMulti(after) ? "multi-sociétés / متعدد الشركات" : "mono-société / شركة واحدة";
                    return "تم التحقق من ترخيصك: لم يتغيّر شيء (" + scope + ")." + Environment.NewLine +
                           "Licence vérifiée : elle est à jour, rien n'a changé (" + scope + ").";
                case LicenseResultKind.Offline:
                    return "لا يوجد اتصال بالإنترنت: تعذّر التحقق. يبقى ترخيصك الحالي صالحًا ولم يتغيّر شيء." + Environment.NewLine +
                           "Pas de connexion Internet : la vérification n'a pas pu se faire. Votre licence actuelle reste valable, rien n'a changé.";
                case LicenseResultKind.NotActivated:
                case LicenseResultKind.NotConfigured:
                    return "لا يوجد ترخيص مفعَّل على هذا الحاسوب." + Environment.NewLine +
                           "Aucune licence n'est activée sur cet ordinateur.";
                default:
                    return "تعذّر التحقق من الترخيص. يبقى ترخيصك الحالي كما هو." + Environment.NewLine +
                           "La licence n'a pas pu être vérifiée. Votre licence actuelle est conservée. " + (result?.Message ?? string.Empty);
            }
        }
    }
}
