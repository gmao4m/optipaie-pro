using System.Collections.Generic;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>Marketing content shown on a locked module's premium page.</summary>
    public sealed class PremiumModuleContent
    {
        public PremiumModuleContent(string key, string title, string description, string iconKey, string[] features)
        {
            Key = key;
            Title = title;
            Description = description;
            IconKey = iconKey;
            Features = features;
        }

        public string Key { get; }
        public string Title { get; }
        public string Description { get; }

        /// <summary>Resource key of the module's icon geometry (see Theme/Icons.xaml).</summary>
        public string IconKey { get; }

        public string[] Features { get; }
    }

    /// <summary>
    /// The premium (upsell) copy for every purchasable module. French, to match the
    /// application's chrome. This is presentation content only — no business logic.
    /// Keys mirror <see cref="ModuleKeys"/> exactly.
    /// </summary>
    public static class PremiumModuleCatalog
    {
        private static readonly Dictionary<string, PremiumModuleContent> Map =
            new Dictionary<string, PremiumModuleContent>
            {
                [ModuleKeys.Ats] = new PremiumModuleContent(
                    ModuleKeys.Ats,
                    "ATS / DRT",
                    "Générez et imprimez automatiquement les déclarations ATS / DRT à partir de vos données de paie et de vos employés existants.",
                    "IconClipboard",
                    new[]
                    {
                        "Génération automatique des déclarations",
                        "Impression professionnelle",
                        "Réutilise les données de paie existantes",
                        "Fait gagner des heures de travail manuel",
                        "Réduit les erreurs de déclaration",
                        "Entièrement intégré à la paie"
                    }),

                [ModuleKeys.Attendance] = new PremiumModuleContent(
                    ModuleKeys.Attendance,
                    "Gestion du pointage",
                    "Suivez la présence, les retards et les heures de travail de vos employés depuis un seul endroit.",
                    "IconClock",
                    new[]
                    {
                        "Pointage quotidien",
                        "Heures de travail",
                        "Retards",
                        "Rapports mensuels de présence",
                        "Intégration avec la paie"
                    }),

                [ModuleKeys.Leave] = new PremiumModuleContent(
                    ModuleKeys.Leave,
                    "Gestion des congés",
                    "Gérez les demandes de congés et les soldes de vacances de vos employés de façon professionnelle.",
                    "IconPlane",
                    new[]
                    {
                        "Congés payés",
                        "Congés maladie",
                        "Soldes de congés",
                        "Historique des congés",
                        "Intégration avec la paie"
                    }),

                [ModuleKeys.Loans] = new PremiumModuleContent(
                    ModuleKeys.Loans,
                    "Prêts & avances",
                    "Gérez les avances sur salaire et les prêts employés avec des retenues automatiques sur la paie.",
                    "IconCard",
                    new[]
                    {
                        "Avances sur salaire",
                        "Prêts employés",
                        "Retenues automatiques",
                        "Échéancier de remboursement",
                        "Soldes restants"
                    }),

                [ModuleKeys.Performance] = new PremiumModuleContent(
                    ModuleKeys.Performance,
                    "Évaluation & promotions",
                    "Évaluez la performance de vos employés et gérez les promotions et les changements de poste.",
                    "IconTrend",
                    new[]
                    {
                        "Évaluations des employés",
                        "Promotions",
                        "Changements de poste",
                        "Historique de performance",
                        "Rapports RH"
                    }),

                [ModuleKeys.Contracts] = new PremiumModuleContent(
                    ModuleKeys.Contracts,
                    "Contrats & renouvellements",
                    "Gérez les contrats de vos employés et leurs renouvellements avec des rappels automatiques.",
                    "IconFileCheck",
                    new[]
                    {
                        "Contrats de travail",
                        "Rappels de renouvellement",
                        "Alertes d'expiration",
                        "Archive des contrats",
                        "Historique complet des contrats"
                    }),

                [ModuleKeys.Training] = new PremiumModuleContent(
                    ModuleKeys.Training,
                    "Formation & cours",
                    "Gérez les formations, les certifications et le développement professionnel de vos employés.",
                    "IconSchool",
                    new[]
                    {
                        "Suivi des formations",
                        "Cours",
                        "Certifications",
                        "Historique des formations",
                        "Développement des employés"
                    }),

                [ModuleKeys.Assets] = new PremiumModuleContent(
                    ModuleKeys.Assets,
                    "Biens & équipements",
                    "Suivez tous les biens de l'entreprise affectés à vos employés.",
                    "IconLaptop",
                    new[]
                    {
                        "Ordinateurs",
                        "Téléphones",
                        "Véhicules",
                        "Affectation du matériel",
                        "Historique des biens"
                    }),

                [ModuleKeys.WorkCertificate] = new PremiumModuleContent(
                    ModuleKeys.WorkCertificate,
                    "Attestation de travail",
                    "Générez et imprimez automatiquement les attestations de travail de vos employés.",
                    "IconCertificate",
                    new[]
                    {
                        "Génération en un clic",
                        "Impression professionnelle",
                        "Informations employé automatiques",
                        "Identité visuelle de l'entreprise",
                        "Gain de temps"
                    }),
            };

        public static PremiumModuleContent For(string key)
        {
            if (key != null && Map.TryGetValue(key, out PremiumModuleContent content))
            {
                return content;
            }

            return new PremiumModuleContent(key ?? string.Empty, key ?? "Module",
                "Ce module premium n'est pas inclus dans votre licence actuelle.", "IconLock",
                new string[0]);
        }
    }
}
