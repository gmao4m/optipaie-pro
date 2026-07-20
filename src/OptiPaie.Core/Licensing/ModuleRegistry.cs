using System;
using System.Collections.Generic;
using System.Linq;

namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Describes one licensable module: its stable key, bilingual display names,
    /// whether it is part of the always-enabled core, and its display order.
    /// </summary>
    public sealed class ModuleDescriptor
    {
        public ModuleDescriptor(string key, string nameFr, string nameAr, bool isCore, int sortOrder)
        {
            Key = key;
            NameFr = nameFr;
            NameAr = nameAr;
            IsCore = isCore;
            SortOrder = sortOrder;
        }

        /// <summary>Stable module key (see <see cref="ModuleKeys"/>).</summary>
        public string Key { get; }

        /// <summary>French display name.</summary>
        public string NameFr { get; }

        /// <summary>Arabic display name.</summary>
        public string NameAr { get; }

        /// <summary>True for the base product, which is always enabled when active.</summary>
        public bool IsCore { get; }

        /// <summary>Order in the navigation.</summary>
        public int SortOrder { get; }

        /// <summary>Returns the display name for the current reading direction.</summary>
        public string DisplayName(bool rightToLeft)
        {
            return rightToLeft ? NameAr : NameFr;
        }
    }

    /// <summary>
    /// The catalogue of modules the desktop knows about. Navigation and license
    /// gating are generated from this registry, so adding a future module means
    /// adding one descriptor here (plus its screen) — never refactoring the shell.
    /// </summary>
    public interface IModuleRegistry
    {
        /// <summary>All modules, in display order.</summary>
        IReadOnlyList<ModuleDescriptor> All { get; }

        /// <summary>Only the purchasable (non-core) modules, in display order.</summary>
        IReadOnlyList<ModuleDescriptor> Upsells { get; }

        /// <summary>Finds a module by key, or null.</summary>
        ModuleDescriptor Find(string key);

        /// <summary>True if the key is a known module.</summary>
        bool Exists(string key);

        /// <summary>True if the key is a core (always-enabled) module.</summary>
        bool IsCore(string key);
    }

    /// <summary>
    /// Canonical, in-memory module registry for the Payroll product. Mirrors the
    /// cloud seed and backend/MODULES.md exactly (same keys, same order).
    /// </summary>
    public sealed class ModuleRegistry : IModuleRegistry
    {
        private readonly List<ModuleDescriptor> _all;
        private readonly Dictionary<string, ModuleDescriptor> _byKey;

        public ModuleRegistry()
        {
            _all = new List<ModuleDescriptor>
            {
                new ModuleDescriptor(ModuleKeys.Payroll,         "Paie",                       "الأجور",            true,  10),
                new ModuleDescriptor(ModuleKeys.Ats,             "ATS / DRT",                  "ATS / DRT",         false, 20),
                new ModuleDescriptor(ModuleKeys.Attendance,      "Gestion du pointage",        "إدارة الحضور",      false, 30),
                new ModuleDescriptor(ModuleKeys.Leave,           "Gestion des congés",         "إدارة العطل",       false, 40),
                new ModuleDescriptor(ModuleKeys.Loans,           "Prêts & avances",            "القروض والتسبيقات", false, 50),
                new ModuleDescriptor(ModuleKeys.Performance,     "Évaluation & promotions",    "التقييم والترقيات", false, 60),
                new ModuleDescriptor(ModuleKeys.Contracts,       "Contrats & renouvellements", "العقود والتجديد",   false, 70),
                new ModuleDescriptor(ModuleKeys.Training,        "Formation & cours",          "التكوين والدورات",  false, 80),
                new ModuleDescriptor(ModuleKeys.Assets,          "Biens & équipements",        "الأصول والمعدات",   false, 90),
                new ModuleDescriptor(ModuleKeys.WorkCertificate, "Attestation de travail",     "شهادة العمل",       false, 100),
            };

            _byKey = _all.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<ModuleDescriptor> All => _all;

        public IReadOnlyList<ModuleDescriptor> Upsells => _all.Where(m => !m.IsCore).ToList();

        public ModuleDescriptor Find(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return _byKey.TryGetValue(key, out ModuleDescriptor descriptor) ? descriptor : null;
        }

        public bool Exists(string key)
        {
            return Find(key) != null;
        }

        public bool IsCore(string key)
        {
            ModuleDescriptor descriptor = Find(key);
            return descriptor != null && descriptor.IsCore;
        }
    }
}
