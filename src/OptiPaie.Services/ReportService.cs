using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;

namespace OptiPaie.Services
{
    /// <summary>
    /// The Reports Center. Every report is a pure read-aggregation over the module
    /// services, returned as a uniform <see cref="ReportTable"/>. No SQL, no writes, and
    /// no contact with the payroll engine.
    /// </summary>
    public sealed class ReportService : IReportService
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        // Report keys.
        public const string Headcount = "headcount";
        public const string Turnover = "turnover";
        public const string Attendance = "attendance";
        public const string LeaveLiability = "leave_liability";
        public const string Loans = "loans";
        public const string Training = "training";
        public const string Assets = "assets";
        public const string Recruitment = "recruitment";

        private readonly ICompanyService _companies;
        private readonly IEmployeeService _employees;
        private readonly IAttendanceService _attendance;
        private readonly ILeaveService _leave;
        private readonly ILoanService _loans;
        private readonly IAtsService _ats;
        private readonly IAssetService _assets;
        private readonly ITrainingService _training;

        public ReportService(
            ICompanyService companies,
            IEmployeeService employees,
            IAttendanceService attendance,
            ILeaveService leave,
            ILoanService loans,
            IAtsService ats,
            IAssetService assets,
            ITrainingService training)
        {
            _companies = Guard.AgainstNull(companies, nameof(companies));
            _employees = Guard.AgainstNull(employees, nameof(employees));
            _attendance = Guard.AgainstNull(attendance, nameof(attendance));
            _leave = Guard.AgainstNull(leave, nameof(leave));
            _loans = Guard.AgainstNull(loans, nameof(loans));
            _ats = Guard.AgainstNull(ats, nameof(ats));
            _assets = Guard.AgainstNull(assets, nameof(assets));
            _training = Guard.AgainstNull(training, nameof(training));
        }

        public IReadOnlyList<ReportDescriptor> GetReports()
        {
            return new List<ReportDescriptor>
            {
                new ReportDescriptor(Headcount, "Effectif (roster)", "Effectif", false),
                new ReportDescriptor(Turnover, "Mouvements du personnel (turnover)", "Effectif", false),
                new ReportDescriptor(Attendance, "Synthèse de présence", "Présence", true),
                new ReportDescriptor(LeaveLiability, "Passif de congés", "Congés", false),
                new ReportDescriptor(Loans, "Encours des prêts", "Prêts", false),
                new ReportDescriptor(Training, "Conformité formation", "Formation", false),
                new ReportDescriptor(Assets, "Inventaire du matériel", "Matériel", false),
                new ReportDescriptor(Recruitment, "Entonnoir de recrutement", "Recrutement", false)
            };
        }

        public ReportTable Build(string reportKey, long companyId, int year, int month)
        {
            Company company = _companies.Get(companyId);
            string companyName = company != null ? company.NameFr : "—";

            switch (reportKey)
            {
                case Headcount: return HeadcountReport(companyId, companyName);
                case Turnover: return TurnoverReport(companyId, companyName, year);
                case Attendance: return AttendanceReport(companyId, companyName, year, month);
                case LeaveLiability: return LeaveLiabilityReport(companyId, companyName, year);
                case Loans: return LoansReport(companyId, companyName);
                case Training: return TrainingReport(companyId, companyName);
                case Assets: return AssetsReport(companyId, companyName);
                case Recruitment: return RecruitmentReport(companyId, companyName);
                default: return new ReportTable { Title = "Rapport inconnu", Subtitle = companyName };
            }
        }

        // ------------------------------------------------------------------ builders

        private ReportTable HeadcountReport(long companyId, string companyName)
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (Employee e in _employees.GetByCompany(companyId, false).OrderBy(x => x.LastNameFr))
            {
                rows.Add(new[]
                {
                    e.Id.ToString("0000", CultureInfo.InvariantCulture),
                    (e.LastNameFr + " " + e.FirstNameFr).Trim(),
                    Blank(e.Department),
                    Blank(e.Poste),
                    ContractLabel(e.ContractType),
                    e.HireDate.ToString("dd/MM/yyyy", Fr),
                    e.BaseSalary.ToString("N2", Fr)
                });
            }

            return new ReportTable
            {
                Title = "Effectif — roster",
                Subtitle = companyName + " · " + rows.Count + " employé(s) actif(s)",
                Columns = new[] { "N°", "Employé", "Département", "Poste", "Contrat", "Embauche", "Salaire de base" },
                NumericColumns = new[] { 6 },
                Rows = rows
            };
        }

        private ReportTable TurnoverReport(long companyId, string companyName, int year)
        {
            var employees = _employees.GetByCompany(companyId, true).ToList(); // include inactive for exits
            var rows = new List<IReadOnlyList<string>>();
            int totalHires = 0, totalExits = 0;

            for (int m = 1; m <= 12; m++)
            {
                int hires = employees.Count(e => e.HireDate.Year == year && e.HireDate.Month == m);
                int exits = employees.Count(e => e.ExitDate.HasValue && e.ExitDate.Value.Year == year && e.ExitDate.Value.Month == m);
                totalHires += hires;
                totalExits += exits;
                rows.Add(new[]
                {
                    Capitalize(Fr.DateTimeFormat.GetMonthName(m)),
                    hires.ToString(),
                    exits.ToString(),
                    (hires - exits).ToString()
                });
            }

            rows.Add(new[] { "TOTAL", totalHires.ToString(), totalExits.ToString(), (totalHires - totalExits).ToString() });

            return new ReportTable
            {
                Title = "Mouvements du personnel — " + year,
                Subtitle = companyName + " · " + totalHires + " entrée(s), " + totalExits + " sortie(s)",
                Columns = new[] { "Mois", "Entrées", "Sorties", "Solde" },
                NumericColumns = new[] { 1, 2, 3 },
                Rows = rows
            };
        }

        private ReportTable AttendanceReport(long companyId, string companyName, int year, int month)
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (AttendanceSummary s in _attendance.GetCompanyMonthlySummary(companyId, year, month))
            {
                rows.Add(new[]
                {
                    Blank(s.EmployeeName),
                    s.PresentDays.ToString(),
                    s.AbsentDays.ToString(),
                    s.LateCount.ToString(),
                    s.LeaveDays.ToString(),
                    s.WorkedHours.ToString("0.##", Fr),
                    s.OvertimeHours.ToString("0.##", Fr)
                });
            }

            return new ReportTable
            {
                Title = "Synthèse de présence",
                Subtitle = companyName + " · " + Capitalize(Fr.DateTimeFormat.GetMonthName(month)) + " " + year,
                Columns = new[] { "Employé", "Présents", "Absents", "Retards", "Congés", "Heures", "H. supp." },
                NumericColumns = new[] { 1, 2, 3, 4, 5, 6 },
                Rows = rows
            };
        }

        private ReportTable LeaveLiabilityReport(long companyId, string companyName, int year)
        {
            var rows = new List<IReadOnlyList<string>>();
            decimal totalRemaining = 0m;
            foreach (LeaveBalance b in _leave.GetCompanyBalances(companyId, year))
            {
                totalRemaining += b.Remaining;
                rows.Add(new[]
                {
                    Blank(b.EmployeeName),
                    b.Entitlement.ToString("0.##", Fr),
                    b.Taken.ToString("0.##", Fr),
                    b.Pending.ToString("0.##", Fr),
                    b.Remaining.ToString("0.##", Fr)
                });
            }

            return new ReportTable
            {
                Title = "Passif de congés — " + year,
                Subtitle = companyName + " · " + totalRemaining.ToString("0.##", Fr) + " jour(s) restants au total",
                Columns = new[] { "Employé", "Droit", "Pris", "En attente", "Restant" },
                NumericColumns = new[] { 1, 2, 3, 4 },
                Rows = rows
            };
        }

        private ReportTable LoansReport(long companyId, string companyName)
        {
            var rows = new List<IReadOnlyList<string>>();
            decimal totalOutstanding = 0m;
            foreach (LoanSummary l in _loans.GetByCompany(companyId))
            {
                if (l.Status == LoanStatus.Active) totalOutstanding += l.Outstanding;
                rows.Add(new[]
                {
                    Blank(l.EmployeeName),
                    l.Principal.ToString("N2", Fr),
                    l.Repaid.ToString("N2", Fr),
                    l.Outstanding.ToString("N2", Fr),
                    LoanStatusLabel(l.Status)
                });
            }

            return new ReportTable
            {
                Title = "Encours des prêts",
                Subtitle = companyName + " · encours actif " + totalOutstanding.ToString("N2", Fr) + " DA",
                Columns = new[] { "Employé", "Montant", "Remboursé", "Reste dû", "Statut" },
                NumericColumns = new[] { 1, 2, 3 },
                Rows = rows
            };
        }

        private ReportTable TrainingReport(long companyId, string companyName)
        {
            var rows = new List<IReadOnlyList<string>>();
            decimal totalCost = 0m;
            foreach (TrainingSummary t in _training.GetByCompany(companyId))
            {
                totalCost += t.Cost;
                rows.Add(new[]
                {
                    Blank(t.Title),
                    Blank(t.Category),
                    TrainingStatusLabel(t.Status),
                    t.ParticipantCount.ToString(),
                    t.CompletedCount.ToString(),
                    t.Cost.ToString("N2", Fr)
                });
            }

            return new ReportTable
            {
                Title = "Conformité formation",
                Subtitle = companyName + " · budget total " + totalCost.ToString("N2", Fr) + " DA",
                Columns = new[] { "Formation", "Domaine", "Statut", "Participants", "Réussis", "Coût" },
                NumericColumns = new[] { 3, 4, 5 },
                Rows = rows
            };
        }

        private ReportTable AssetsReport(long companyId, string companyName)
        {
            var rows = new List<IReadOnlyList<string>>();
            decimal totalValue = 0m;
            foreach (AssetSummary a in _assets.GetByCompany(companyId))
            {
                totalValue += a.PurchaseValue;
                rows.Add(new[]
                {
                    Blank(a.Name),
                    AssetCategoryLabel(a.Category),
                    Blank(a.SerialNumber),
                    a.PurchaseValue.ToString("N2", Fr),
                    Blank(a.HolderName),
                    AssetStatusLabel(a.Status)
                });
            }

            return new ReportTable
            {
                Title = "Inventaire du matériel",
                Subtitle = companyName + " · valeur totale " + totalValue.ToString("N2", Fr) + " DA",
                Columns = new[] { "Matériel", "Catégorie", "N° série", "Valeur", "Détenteur", "Statut" },
                NumericColumns = new[] { 3 },
                Rows = rows
            };
        }

        private ReportTable RecruitmentReport(long companyId, string companyName)
        {
            var rows = new List<IReadOnlyList<string>>();
            int totalCandidates = 0, totalHired = 0;
            foreach (JobPostingSummary p in _ats.GetPostingsByCompany(companyId))
            {
                totalCandidates += p.CandidateCount;
                totalHired += p.HiredCount;
                rows.Add(new[]
                {
                    Blank(p.Title),
                    JobStatusLabel(p.Status),
                    p.Positions.ToString(),
                    p.CandidateCount.ToString(),
                    p.HiredCount.ToString()
                });
            }

            return new ReportTable
            {
                Title = "Entonnoir de recrutement",
                Subtitle = companyName + " · " + totalCandidates + " candidat(s), " + totalHired + " recruté(s)",
                Columns = new[] { "Poste", "Statut", "Postes", "Candidats", "Recrutés" },
                NumericColumns = new[] { 2, 3, 4 },
                Rows = rows
            };
        }

        // ------------------------------------------------------------------ labels

        private static string Blank(string v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Fr) + s.Substring(1);

        private static string ContractLabel(ContractType t)
        {
            switch (t)
            {
                case ContractType.Cdi: return "CDI";
                case ContractType.Cdd: return "CDD";
                case ContractType.Apprenticeship: return "Apprentissage";
                case ContractType.Internship: return "Stage";
                default: return "Autre";
            }
        }

        private static string LoanStatusLabel(LoanStatus s)
        {
            switch (s)
            {
                case LoanStatus.Active: return "En cours";
                case LoanStatus.Settled: return "Soldé";
                case LoanStatus.Suspended: return "Suspendu";
                default: return "Annulé";
            }
        }

        private static string TrainingStatusLabel(TrainingStatus s)
        {
            switch (s)
            {
                case TrainingStatus.Planned: return "Planifiée";
                case TrainingStatus.Ongoing: return "En cours";
                case TrainingStatus.Completed: return "Terminée";
                default: return "Annulée";
            }
        }

        private static string AssetStatusLabel(AssetStatus s)
        {
            switch (s)
            {
                case AssetStatus.Available: return "Disponible";
                case AssetStatus.Assigned: return "Attribué";
                case AssetStatus.UnderRepair: return "En réparation";
                default: return "Réformé";
            }
        }

        private static string AssetCategoryLabel(AssetCategory c)
        {
            switch (c)
            {
                case AssetCategory.Laptop: return "Ordinateur";
                case AssetCategory.Phone: return "Téléphone";
                case AssetCategory.Vehicle: return "Véhicule";
                case AssetCategory.Uniform: return "Tenue / EPI";
                case AssetCategory.Tool: return "Outillage";
                default: return "Autre";
            }
        }

        private static string JobStatusLabel(JobStatus s)
        {
            switch (s)
            {
                case JobStatus.Open: return "Ouverte";
                case JobStatus.Closed: return "Fermée";
                default: return "Pourvue";
            }
        }
    }
}
