using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;

namespace OptiPaie.Services
{
    /// <summary>
    /// The signature of the trial demo company as <see cref="DemoDataSeeder"/> creates it. A company is the
    /// demo ONLY when it carries the IsDemo flag OR its four (fictitious) legal identifiers — NIF, NIS, RC,
    /// article d'imposition — are exactly the seeded ones; AND, in every case, its employees are exactly the
    /// 20 seeded people; AND its departments are among the seeded ones plus the app's default "Générale".
    /// The name, CNAS number and e-mail are deliberately NOT required: they are display/contact fields a
    /// trial user edits (seen on a real database: the CNAS number had been changed), and the seeder's own
    /// CNAS number changed once. The legal identifiers and the roster never changed since the first seeder.
    /// A name alone never matches. DemoDataServiceTests seeds the real seeder and asserts this still matches.
    /// </summary>
    public static class DemoFingerprint
    {
        public const string NameFr = DemoDataSeeder.DemoCompanyName; // "SARL Atlas Industrie"
        public const string Nif = "000916025478931";
        public const string Nis = "000916025478931";
        public const string Rc = "09/00-1245789 B 16";
        public const string ArticleImposition = "16050124789";
        public const string CnasEmployerNumber = "091-024-5789";
        public const string Email = "contact@atlas-industrie.dz";

        /// <summary>The 20 seeded employees, "LAST|First".</summary>
        public static readonly IReadOnlyCollection<string> Employees = new[]
        {
            "BENALI|Karim", "HAMADI|Sofiane", "ZEROUAL|Nadia", "MEZIANE|Yacine",
            "BOUDIAF|Rachid", "KADDOUR|Amine", "SAADI|Farid", "HADDAD|Bilal", "CHERIF|Mourad",
            "TOUATI|Lila", "BELKACEM|Sabrina", "OULD ALI|Nassim", "FERHAT|Yasmine",
            "AMRANI|Salima", "GHERBI|Karima", "LOUNICI|Djamel", "REBAI|Hafida",
            "BOUZID|Walid", "SLIMANI|Ryad", "MEDJANI|Imene"
        };

        /// <summary>The seeded departments plus "Générale", which DepartmentService adds to every company.</summary>
        public static readonly IReadOnlyCollection<string> Departments = new[] { "Production", "Commercial", "Administration", "Informatique", "Générale" };
    }

    /// <summary>The trial demo company that was found, and how it was recognised.</summary>
    public sealed class DemoCompanyInfo
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public bool IsDeleted { get; set; }
        /// <summary>"flag" (IsDemo set at seed time) or "fingerprint" (older database, full match).</summary>
        public string MatchedBy { get; set; }
    }

    /// <summary>What a demo removal would delete, with counts.</summary>
    public sealed class DemoDeletionPreview
    {
        public DemoCompanyInfo Company { get; set; }
        public IReadOnlyList<TableRows> Rows { get; set; }
        public long Count(string table) => Rows.Where(r => r.Table == table).Sum(r => r.Rows);
        public long TotalRows => Rows.Sum(r => r.Rows);
        /// <summary>True when the demo is the only company left: removal is refused.</summary>
        public bool IsOnlyCompany { get; set; }
    }

    /// <summary>The result of a demo removal.</summary>
    public sealed class DemoDeletionOutcome
    {
        public string BackupPath { get; set; }
        public IReadOnlyList<TableRows> Deleted { get; set; }
        public long? RemainingCompanyId { get; set; }
    }

    /// <summary>
    /// Finds and removes the trial demo company. Identification is deliberately strict: better to leave
    /// demo data behind than to delete real data. Removal is administrator-only, refused when the demo is
    /// the only company, preceded by an automatic backup, and done in ONE transaction.
    /// </summary>
    public sealed class DemoDataService
    {
        private readonly IUnitOfWorkFactory _uowFactory;
        private readonly IBackupService _backup;
        private readonly IAccessContext _access;

        public DemoDataService(IUnitOfWorkFactory uowFactory, IBackupService backup, IAccessContext access)
        {
            _uowFactory = Guard.AgainstNull(uowFactory, nameof(uowFactory));
            _backup = Guard.AgainstNull(backup, nameof(backup));
            _access = access;
        }

        /// <summary>The demo company, or null when none matches completely (then the button stays hidden).</summary>
        public DemoCompanyInfo FindDemoCompany()
        {
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                foreach (CompanyFingerprint c in uow.DemoData.GetCompanyFingerprints())
                {
                    bool identifiers =
                        Same(c.Nif, DemoFingerprint.Nif) && Same(c.Nis, DemoFingerprint.Nis) &&
                        Same(c.Rc, DemoFingerprint.Rc) && Same(c.ArticleImposition, DemoFingerprint.ArticleImposition);

                    // A name alone is never enough: the flag (set by the seeder) or ALL four seeded legal identifiers.
                    if (!c.IsDemo && !identifiers) continue;

                    // …and in every case the employees must be EXACTLY the seeded roster (a real person added to
                    // the demo company means real data lives there — leave it alone) and no foreign department.
                    var names = new HashSet<string>(uow.DemoData.GetEmployeeNames(c.Id).Select(Norm));
                    if (!names.SetEquals(DemoFingerprint.Employees.Select(Norm))) continue;
                    if (!uow.DemoData.GetDepartmentNames(c.Id).Select(Norm)
                            .All(d => DemoFingerprint.Departments.Select(Norm).Contains(d))) continue;

                    return new DemoCompanyInfo
                    {
                        Id = c.Id, Name = c.NameFr, IsDeleted = c.IsDeleted, MatchedBy = c.IsDemo ? "flag" : "fingerprint"
                    };
                }
            }
            return null;
        }

        /// <summary>What removing the demo would delete; null when there is no demo data.</summary>
        public DemoDeletionPreview Preview()
        {
            DemoCompanyInfo demo = FindDemoCompany();
            if (demo == null) return null;
            using (IUnitOfWork uow = _uowFactory.Create())
            {
                return new DemoDeletionPreview
                {
                    Company = demo,
                    Rows = uow.DemoData.CountDependents(demo.Id),
                    IsOnlyCompany = !OtherActiveCompanies(uow, demo.Id).Any()
                };
            }
        }

        /// <summary>Backs up the database, then deletes the demo company and every dependent row.</summary>
        public Result<DemoDeletionOutcome> Delete()
        {
            AccessPolicy.Require(_access, Permission.ManageSettings); // administrators only — fails closed

            DemoCompanyInfo demo = FindDemoCompany(); // re-identified here, never trusted from the caller
            if (demo == null)
                return Result.Fail<DemoDeletionOutcome>(
                    "Aucune donnée de démonstration reconnue. Rien n'a été supprimé." + Environment.NewLine +
                    "لم يُتعرَّف على أيّ بيانات تجريبية. لم يُحذَف شيء.", "Demo_NotFound");

            long? remaining;
            using (IUnitOfWork uow = _uowFactory.Create())
                remaining = OtherActiveCompanies(uow, demo.Id).Select(c => (long?)c.Id).FirstOrDefault();
            if (remaining == null)
                return Result.Fail<DemoDeletionOutcome>(
                    "La société de démonstration est la seule entreprise de la base : le logiciel en a besoin d'au moins une. " +
                    "Créez d'abord votre propre entreprise, puis supprimez la société de démonstration." + Environment.NewLine +
                    "شركة العرض التجريبي هي الشركة الوحيدة في قاعدة البيانات، والبرنامج يحتاج إلى شركة واحدة على الأقلّ. " +
                    "أنشئ شركتك أوّلًا، ثمّ احذف شركة العرض التجريبي.", "Demo_OnlyCompany");

            // Irreversible otherwise: a full backup FIRST. No backup, no deletion.
            Result<BackupRecord> backup = _backup.Backup(BackupType.Manual);
            if (backup.IsFailure || backup.Value == null)
                return Result.Fail<DemoDeletionOutcome>(
                    "La sauvegarde préalable a échoué : rien n'a été supprimé. " + backup.Error + Environment.NewLine +
                    "فشل إنشاء النسخة الاحتياطية المسبقة: لم يُحذَف شيء.", "Demo_BackupFailed");

            using (IUnitOfWork uow = _uowFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    IReadOnlyList<TableRows> deleted = uow.DemoData.DeleteCompanyAndDependents(demo.Id);
                    CleanSettings(uow, demo.Id, remaining.Value);
                    uow.Commit();
                    return Result.Ok(new DemoDeletionOutcome
                    {
                        BackupPath = backup.Value.FilePath, Deleted = deleted, RemainingCompanyId = remaining
                    });
                }
                catch (Exception ex)
                {
                    uow.Rollback();
                    return Result.Fail<DemoDeletionOutcome>(
                        "La suppression a échoué et a été entièrement annulée : rien n'a changé. " + ex.Message + Environment.NewLine +
                        "فشل الحذف وأُلغيَ بالكامل: لم يتغيّر شيء.", "Demo_DeleteFailed");
                }
            }
        }

        private static IEnumerable<CompanyFingerprint> OtherActiveCompanies(IUnitOfWork uow, long demoId) =>
            uow.DemoData.GetCompanyFingerprints().Where(c => c.Id != demoId && !c.IsDeleted);

        /// <summary>Settings that point at the removed company: the default company and the legacy recent list.</summary>
        private static void CleanSettings(IUnitOfWork uow, long demoId, long remainingId)
        {
            AppSetting def = uow.AppSettings.Get(SettingKeys.DefaultCompanyId);
            if (def != null && long.TryParse(def.SettingValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long d) && d == demoId)
                uow.AppSettings.Upsert(SettingKeys.DefaultCompanyId, remainingId.ToString(CultureInfo.InvariantCulture));

            AppSetting recent = uow.AppSettings.Get("RECENT_COMPANY");
            if (recent != null && !string.IsNullOrWhiteSpace(recent.SettingValue))
            {
                try
                {
                    JArray list = JArray.Parse(recent.SettingValue);
                    var keep = new JArray(list.Where(t => !(t is JObject o && (long?)o["Id"] == demoId)));
                    if (keep.Count != list.Count) uow.AppSettings.Upsert("RECENT_COMPANY", keep.ToString(Newtonsoft.Json.Formatting.None));
                }
                catch (Exception) { /* a legacy convenience list: never block the removal on it */ }
            }
        }

        private static bool Same(string a, string b) =>
            string.Equals((a ?? string.Empty).Trim(), b, StringComparison.OrdinalIgnoreCase);

        private static string Norm(string s) => (s ?? string.Empty).Trim().ToUpperInvariant();
    }
}
