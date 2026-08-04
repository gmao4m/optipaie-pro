using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Builds a full, internally-consistent Algerian demo dataset by driving the real
    /// module services (so every cross-module effect is genuine: activating a contract
    /// syncs the employee, approving leave writes attendance, completing a review feeds the
    /// dashboard). Intended for the trial/demo experience only — it refuses to run when the
    /// database already holds a company, so it can never touch real data. Never writes
    /// payroll calculations; the payslip is generated live from the seeded employee data.
    /// </summary>
    public sealed class DemoDataSeeder
    {
        private readonly ICompanyService _companies;
        private readonly IEmployeeService _employees;
        private readonly IContractService _contracts;
        private readonly IAttendanceService _attendance;
        private readonly ILeaveService _leave;
        private readonly ILoanService _loans;
        private readonly IAssetService _assets;
        private readonly ITrainingService _training;
        private readonly IWorkCertificateService _certificates;
        private readonly IPerformanceService _performance;

        public DemoDataSeeder(
            ICompanyService companies,
            IEmployeeService employees,
            IContractService contracts,
            IAttendanceService attendance,
            ILeaveService leave,
            ILoanService loans,
            IAssetService assets,
            ITrainingService training,
            IWorkCertificateService certificates,
            IPerformanceService performance)
        {
            _companies = companies;
            _employees = employees;
            _contracts = contracts;
            _attendance = attendance;
            _leave = leave;
            _loans = loans;
            _assets = assets;
            _training = training;
            _certificates = certificates;
            _performance = performance;
        }

        /// <summary>The demo company name — used to detect whether the demo is already present.</summary>
        public const string DemoCompanyName = "SARL Atlas Industrie";

        /// <summary>True when there is no company yet (a fresh install / empty demo DB).</summary>
        public bool IsDatabaseEmpty()
        {
            return _companies.GetAll().Count == 0;
        }

        /// <summary>True when the Algerian demo company is already present.</summary>
        public bool HasDemoCompany()
        {
            foreach (Company c in _companies.GetAll())
            {
                if (string.Equals(c.NameFr, DemoCompanyName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Ensures the demo dataset is present for the trial/demo experience. If the demo
        /// company is missing but the database holds OTHER data (e.g. leftover test companies),
        /// that data is hidden (soft-deleted) and the Algerian demo is seeded fresh. Only ever
        /// called in trial mode — a licensed install is never touched.
        /// </summary>
        public Result<long> EnsureDemo()
        {
            if (HasDemoCompany())
            {
                return Result.Ok(0L);
            }

            try
            {
                // Hide any leftover companies/employees so the demo starts from a clean slate.
                foreach (Company c in _companies.GetAll())
                {
                    foreach (Employee e in _employees.GetByCompany(c.Id, true))
                    {
                        _employees.Delete(e.Id);
                    }
                    _companies.Delete(c.Id);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail<long>("Réinitialisation de la démo impossible : " + ex.Message, "Demo_ResetFailed");
            }

            return Seed();
        }

        /// <summary>
        /// Seeds the demo company and all its interconnected data. No-op (returns 0) if the
        /// database already contains a company, so a real install is never overwritten.
        /// </summary>
        public Result<long> Seed()
        {
            if (!IsDatabaseEmpty())
            {
                return Result.Ok(0L);
            }

            try
            {
                DateTime today = DateTime.Today;
                DateTime firstOfMonth = new DateTime(today.Year, today.Month, 1);
                DateTime demoMonth = firstOfMonth.AddMonths(-1); // the previous full month
                int reviewYear = today.Year - 1;

                long companyId = CreateCompany();
                List<EmpSpec> roster = CreateEmployees(companyId);
                var by = roster.ToDictionary(e => e.Code, e => e);

                CreateContracts(roster, today);
                ConfigureDepartments(companyId, by);
                CreateAttendance(roster, demoMonth);
                CreateLeave(by, demoMonth, today);
                CreatePerformance(companyId, by, reviewYear, today);
                CreateLoans(by, today);
                CreateAssets(companyId, by, today);
                CreateTraining(companyId, roster, by, today);
                CreateCertificates(by, today);

                return Result.Ok(companyId);
            }
            catch (Exception ex)
            {
                return Result.Fail<long>("Échec du chargement des données de démonstration : " + ex.Message, "Demo_SeedFailed");
            }
        }

        // ===== company ==========================================================

        private long CreateCompany()
        {
            var company = new Company
            {
                NameFr = "SARL Atlas Industrie",
                NameAr = "ش.ذ.م.م أطلس للصناعة",
                LegalForm = "SARL",
                AddressFr = "Zone industrielle, Lot 24, Boufarik, Blida",
                AddressAr = "المنطقة الصناعية، رقم 24، بوفاريك، البليدة",
                Nif = "000916025478931",
                Nis = "000916025478931",
                Rc = "09/00-1245789 B 16",
                ArticleImposition = "16050124789",
                CnasEmployerNumber = "091-024-5789",   // 10 digits (CNAS employer-number rule)
                Bank = "BEA - Agence Boufarik",
                BankAccount = "002 00456 1120045789 12",
                Currency = "DZD",
                Phone = "025 41 22 30",
                Email = "contact@atlas-industrie.dz",
                Logo = BuildLogo()
            };

            return Must(_companies.Create(company), "création de l'entreprise");
        }

        // ===== employees ========================================================

        private List<EmpSpec> Roster()
        {
            return new List<EmpSpec>
            {
                // Managers (one per department).
                new EmpSpec("benali", "BENALI", "Karim", "بن علي", "كريم", Gender.Male, "Production", "Chef de production", 95000m, MaritalStatus.Married, 3, 2016, ContractType.Cdi, true),
                new EmpSpec("hamadi", "HAMADI", "Sofiane", "حمادي", "سفيان", Gender.Male, "Commercial", "Directeur commercial", 98000m, MaritalStatus.Married, 2, 2017, ContractType.Cdi, true),
                new EmpSpec("zeroual", "ZEROUAL", "Nadia", "زروال", "نادية", Gender.Female, "Administration", "Responsable administratif", 90000m, MaritalStatus.Married, 2, 2015, ContractType.Cdi, true),
                new EmpSpec("meziane", "MEZIANE", "Yacine", "مزيان", "ياسين", Gender.Male, "Informatique", "Responsable informatique", 92000m, MaritalStatus.Single, 0, 2018, ContractType.Cdi, true),

                // Production.
                new EmpSpec("boudiaf", "BOUDIAF", "Rachid", "بوضياف", "رشيد", Gender.Male, "Production", "Opérateur de production", 42000m, MaritalStatus.Married, 4, 2019, ContractType.Cdi, false),
                new EmpSpec("kaddour", "KADDOUR", "Amine", "قدور", "أمين", Gender.Male, "Production", "Opérateur de production", 40000m, MaritalStatus.Single, 0, 2020, ContractType.Cdi, false),
                new EmpSpec("saadi", "SAADI", "Farid", "سعدي", "فريد", Gender.Male, "Production", "Technicien de maintenance", 48000m, MaritalStatus.Married, 2, 2018, ContractType.Cdi, false),
                new EmpSpec("haddad", "HADDAD", "Bilal", "حداد", "بلال", Gender.Male, "Production", "Opérateur de production", 39000m, MaritalStatus.Single, 0, 2023, ContractType.Cdd, false),
                new EmpSpec("cherif", "CHERIF", "Mourad", "شريف", "مراد", Gender.Male, "Production", "Magasinier", 43000m, MaritalStatus.Married, 3, 2021, ContractType.Cdi, false),

                // Commercial.
                new EmpSpec("touati", "TOUATI", "Lila", "تواتي", "ليلى", Gender.Female, "Commercial", "Commerciale", 55000m, MaritalStatus.Single, 0, 2019, ContractType.Cdi, false),
                new EmpSpec("belkacem", "BELKACEM", "Sabrina", "بلقاسم", "صابرينة", Gender.Female, "Commercial", "Commerciale", 52000m, MaritalStatus.Married, 1, 2021, ContractType.Cdi, false),
                new EmpSpec("ouldali", "OULD ALI", "Nassim", "ولد علي", "نسيم", Gender.Male, "Commercial", "Commercial terrain", 50000m, MaritalStatus.Single, 0, 2022, ContractType.Cdd, false),
                new EmpSpec("ferhat", "FERHAT", "Yasmine", "فرحات", "ياسمين", Gender.Female, "Commercial", "Chargée de clientèle", 47000m, MaritalStatus.Married, 1, 2020, ContractType.Cdi, false),

                // Administration.
                new EmpSpec("amrani", "AMRANI", "Salima", "عمراني", "سليمة", Gender.Female, "Administration", "Comptable", 60000m, MaritalStatus.Married, 2, 2017, ContractType.Cdi, false),
                new EmpSpec("gherbi", "GHERBI", "Karima", "غربي", "كريمة", Gender.Female, "Administration", "Assistante RH", 48000m, MaritalStatus.Single, 0, 2019, ContractType.Cdi, false),
                new EmpSpec("lounici", "LOUNICI", "Djamel", "لونيسي", "جمال", Gender.Male, "Administration", "Agent administratif", 41000m, MaritalStatus.Married, 3, 2021, ContractType.Cdi, false),
                new EmpSpec("rebai", "REBAI", "Hafida", "رباعي", "حفيظة", Gender.Female, "Administration", "Standardiste", 38000m, MaritalStatus.Single, 0, today: true), // probation, recent hire

                // Informatique.
                new EmpSpec("bouzid", "BOUZID", "Walid", "بوزيد", "وليد", Gender.Male, "Informatique", "Développeur", 72000m, MaritalStatus.Single, 0, 2020, ContractType.Cdi, false),
                new EmpSpec("slimani", "SLIMANI", "Ryad", "سليماني", "رياض", Gender.Male, "Informatique", "Administrateur systèmes", 68000m, MaritalStatus.Married, 1, 2019, ContractType.Cdi, false),
                new EmpSpec("medjani", "MEDJANI", "Imene", "مجاني", "إيمان", Gender.Female, "Informatique", "Développeuse", 65000m, MaritalStatus.Single, 0, 2022, ContractType.Cdd, false),
            };
        }

        private List<EmpSpec> CreateEmployees(long companyId)
        {
            List<EmpSpec> roster = Roster();
            int n = 0;
            foreach (EmpSpec s in roster)
            {
                var employee = new Employee
                {
                    CompanyId = companyId,
                    LastNameFr = s.LastFr,
                    FirstNameFr = s.FirstFr,
                    LastNameAr = s.LastAr,
                    FirstNameAr = s.FirstAr,
                    Gender = s.Gender,
                    Department = s.Dept,
                    Poste = s.Poste,
                    Category = s.IsManager ? "Cadre" : "Exécution",
                    BaseSalary = s.Salary,
                    MaritalStatus = s.Marital,
                    Dependents = s.Dependents,
                    ContractType = s.Contract,
                    PaymentMode = PaymentMode.BankTransfer,
                    HireDate = s.HireDate,
                    BirthDate = new DateTime(1985 + (n % 12), 1 + (n % 11), 1 + (n % 27)),
                    Nss = "0" + (1985 + (n % 12)).ToString() + (1000000 + n).ToString(),   // 12 digits (NSS rule)
                    NationalId = (109000000000000000L + n).ToString(),
                    Rib = "002004561120" + (100000 + n).ToString() + (10 + (n % 89)).ToString(),
                    IsActive = true
                };

                s.Id = Must(_employees.Create(employee), "création de l'employé " + s.LastFr);
                n++;
            }

            return roster;
        }

        // ===== contracts ========================================================

        private void CreateContracts(List<EmpSpec> roster, DateTime today)
        {
            int cddSeq = 0;
            foreach (EmpSpec s in roster)
            {
                DateTime start = s.HireDate;
                DateTime? end = null;
                int trial = 0;

                if (s.Code == "haddad")
                {
                    // CDD nearing its end — triggers the expiry alert (within 30 days).
                    start = today.AddDays(-700);
                    end = today.AddDays(20);
                }
                else if (s.Code == "rebai")
                {
                    // Active probation (période d'essai) — hired two months ago, 90-day trial.
                    start = today.AddDays(-60);
                    end = today.AddDays(305);
                    trial = 90;
                }
                else if (s.Contract == ContractType.Cdd)
                {
                    // Upcoming renewals (not expired) — spread over the next few months so the
                    // deadlines widget shows healthy renewals to process, never overdue ones.
                    end = today.AddDays(45 + cddSeq * 35);
                    cddSeq++;
                }

                var contract = new EmploymentContract
                {
                    EmployeeId = s.Id,
                    Type = s.Contract,
                    Position = s.Poste,
                    BaseSalary = s.Salary,
                    StartDate = start,
                    EndDate = end,
                    TrialPeriodDays = trial,
                    SignedDate = start,
                    Reference = "CT-" + s.LastFr.Substring(0, Math.Min(3, s.LastFr.Length)).ToUpperInvariant() + "-" + start.Year,
                    Notes = "Contrat de démonstration"
                };

                long contractId = Must(_contracts.Save(contract), "contrat de " + s.LastFr);
                Must(_contracts.Activate(contractId), "activation du contrat de " + s.LastFr);
            }
        }

        // ===== evaluation templates (one per department; weighted + simple modes) ===

        private void ConfigureDepartments(long companyId, Dictionary<string, EmpSpec> by)
        {
            // Production — weighted, mixes behavioural + a productivity KPI.
            SaveDeptTemplate(companyId, "Production", WeightingMode.Weighted, isDefault: true, new[]
            {
                Crit("Qualité du travail", CriterionCategory.Behavioral, ScoreType.Stars5, 35m),
                Kpi("Productivité (pièces / objectif)", 25m, 100m),
                Crit("Respect des consignes de sécurité", CriterionCategory.Behavioral, ScoreType.Stars5, 25m),
                Crit("Assiduité", CriterionCategory.Behavioral, ScoreType.Stars5, 15m)
            });

            // Commercial — weighted, sales KPI carries the most weight.
            SaveDeptTemplate(companyId, "Commercial", WeightingMode.Weighted, isDefault: false, new[]
            {
                Kpi("Objectifs de vente (réalisé / cible)", 50m, 100m),
                Crit("Relation client", CriterionCategory.Behavioral, ScoreType.Stars5, 20m),
                Crit("Assiduité", CriterionCategory.Behavioral, ScoreType.Stars5, 15m),
                Crit("Esprit d'équipe", CriterionCategory.Behavioral, ScoreType.Stars5, 15m)
            });

            // Administration — simple mode (all criteria equal), showcases the other mode.
            SaveDeptTemplate(companyId, "Administration", WeightingMode.Simple, isDefault: false, new[]
            {
                Crit("Exactitude des tâches", CriterionCategory.Administrative, ScoreType.Stars5, 25m),
                Kpi("Respect des délais (%)", 25m, 100m),
                Crit("Réactivité", CriterionCategory.Behavioral, ScoreType.Stars5, 25m),
                Crit("Assiduité", CriterionCategory.Behavioral, ScoreType.Stars5, 25m)
            });

            // Informatique — weighted, technical craft + an incidents KPI.
            SaveDeptTemplate(companyId, "Informatique", WeightingMode.Weighted, isDefault: false, new[]
            {
                Crit("Qualité du travail / du code", CriterionCategory.Technical, ScoreType.Stars5, 35m),
                Kpi("Incidents résolus (réalisé / cible)", 25m, 100m),
                Crit("Respect des délais de projet", CriterionCategory.Behavioral, ScoreType.Stars5, 20m),
                Crit("Évolution des compétences", CriterionCategory.Technical, ScoreType.Stars5, 20m)
            });
        }

        private void SaveDeptTemplate(long companyId, string dept, WeightingMode mode, bool isDefault, EvalCriterion[] criteria)
        {
            var template = new EvalTemplate
            {
                CompanyId = companyId,
                Department = dept,
                Name = "Grille — " + dept,
                Description = "Modèle d'évaluation du département " + dept + ".",
                WeightingMode = mode,
                IsDefault = isDefault
            };
            Must(_performance.SaveTemplate(template, criteria), "modèle du département " + dept);
        }

        private static EvalCriterion Crit(string name, CriterionCategory category, ScoreType type, decimal weight) =>
            new EvalCriterion { Name = name, Category = category, ScoreType = type, WeightPercent = weight };

        private static EvalCriterion Kpi(string name, decimal weight, decimal target) =>
            new EvalCriterion { Name = name, Category = CriterionCategory.Kpi, ScoreType = ScoreType.Percent, WeightPercent = weight, KpiTarget = target, HigherIsBetter = true };

        // ===== attendance (a full previous month) ===============================

        private void CreateAttendance(List<EmpSpec> roster, DateTime demoMonth)
        {
            int year = demoMonth.Year, month = demoMonth.Month;
            int days = DateTime.DaysInMonth(year, month);

            var entries = new List<AttendanceDayStatus>();
            int idx = 0;
            foreach (EmpSpec s in roster)
            {
                for (int d = 1; d <= days; d++)
                {
                    var date = new DateTime(year, month, d);
                    if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday)
                    {
                        continue; // Algerian rest days
                    }

                    int seed = d + idx;
                    AttendanceStatus status =
                        seed % 13 == 0 ? AttendanceStatus.Absent :
                        seed % 7 == 0 ? AttendanceStatus.Late :
                        seed % 17 == 0 ? AttendanceStatus.Mission :
                        AttendanceStatus.Present;

                    entries.Add(new AttendanceDayStatus(s.Id, date, status));
                }
                idx++;
            }

            Must(_attendance.SetDayStatusBulk(entries), "présence du mois");
        }

        // ===== leave (approved / pending / rejected) ============================

        private void CreateLeave(Dictionary<string, EmpSpec> by, DateTime demoMonth, DateTime today)
        {
            int y = demoMonth.Year, m = demoMonth.Month;

            // Approved annual leave — writes 5 days into attendance as "Congé".
            Approve(new LeaveRequest
            {
                EmployeeId = by["touati"].Id,
                Type = LeaveType.Annual,
                StartDate = new DateTime(y, m, 10),
                EndDate = new DateTime(y, m, 14),
                Reason = "Congé annuel"
            });

            // Approved sick leave (2 days).
            Approve(new LeaveRequest
            {
                EmployeeId = by["boudiaf"].Id,
                Type = LeaveType.Sick,
                StartDate = new DateTime(y, m, 20),
                EndDate = new DateTime(y, m, 21),
                Reason = "Arrêt maladie"
            });

            // Pending annual leave (future).
            _leave.Save(new LeaveRequest
            {
                EmployeeId = by["bouzid"].Id,
                Type = LeaveType.Annual,
                StartDate = today.AddDays(15),
                EndDate = today.AddDays(22),
                Reason = "Congé annuel"
            });

            // Pending maternity leave.
            _leave.Save(new LeaveRequest
            {
                EmployeeId = by["ferhat"].Id,
                Type = LeaveType.Maternity,
                StartDate = today.AddDays(30),
                EndDate = today.AddDays(30 + 98),
                Reason = "Congé de maternité"
            });

            // Rejected unpaid leave.
            Result<long> rejected = _leave.Save(new LeaveRequest
            {
                EmployeeId = by["kaddour"].Id,
                Type = LeaveType.Unpaid,
                StartDate = new DateTime(y, m, 5),
                EndDate = new DateTime(y, m, 9),
                Reason = "Convenance personnelle"
            });
            if (rejected.IsSuccess)
            {
                _leave.Reject(rejected.Value, "Effectif insuffisant sur la période");
            }
        }

        private void Approve(LeaveRequest request)
        {
            Result<long> saved = _leave.Save(request);
            if (saved.IsSuccess)
            {
                _leave.Approve(saved.Value, "Accordé");
            }
        }

        // ===== performance (templates seeded above; now behaviour + periods + evaluations) ==

        private void CreatePerformance(long companyId, Dictionary<string, EmpSpec> by, int year, DateTime today)
        {
            // 1. A continuous 👍/👎 behaviour log — facts that ground the evaluations.
            CreateBehaviors(companyId, by, today);

            // 2. A deterministic band per employee (spread), improving over the years — except
            //    one employee who declines, so the "en baisse" alert has something to catch.
            var ordered = by.Values.OrderBy(s => s.Id).ToList();
            var employeeIds = ordered.Select(s => s.Id).ToList();
            var baseBand = new Dictionary<long, decimal>();
            for (int i = 0; i < ordered.Count; i++)
                baseBand[ordered[i].Id] = 0.60m + (i % 8) * 0.045m;   // 0.60 .. 0.915
            long declining = by["cherif"].Id;

            decimal BandFor(long empId, int idx)  // idx: 0 oldest .. 2 most recent
            {
                decimal b = baseBand[empId];
                if (empId == declining)
                    return new[] { b + 0.06m, b, b - 0.10m }[idx];
                return Math.Min(0.98m, b - 0.06m + idx * 0.03m);
            }

            // 3. Three finalised annual periods → history, trend and the reports.
            SeedAnnualPeriod(companyId, "Évaluation " + (year - 2), year - 2, employeeIds, id => BandFor(id, 0));
            SeedAnnualPeriod(companyId, "Évaluation " + (year - 1), year - 1, employeeIds, id => BandFor(id, 1));
            SeedAnnualPeriod(companyId, "Évaluation " + year, year, employeeIds, id => BandFor(id, 2));

            // 4. A current OPEN monthly period — a few done, the rest pending: the live flow.
            var open = new EvalPeriod
            {
                CompanyId = companyId,
                Name = "Campagne " + today.Year + " — en cours",
                Cadence = PeriodCadence.Monthly,
                StartDate = new DateTime(today.Year, today.Month, 1),
                EndDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
                Status = PeriodStatus.Open
            };
            long openId = Must(_performance.SavePeriod(open), "période courante");
            foreach (long id in employeeIds.Take(6))
                SeedEvaluation(openId, id, BandFor(id, 2));
        }

        private long SeedAnnualPeriod(long companyId, string name, int y, List<long> employeeIds, Func<long, decimal> bandFor)
        {
            var period = new EvalPeriod
            {
                CompanyId = companyId,
                Name = name,
                Cadence = PeriodCadence.Yearly,
                StartDate = new DateTime(y, 1, 1),
                EndDate = new DateTime(y, 12, 31),
                Status = PeriodStatus.Open
            };
            long pid = Must(_performance.SavePeriod(period), "période " + name);
            foreach (long empId in employeeIds)
                SeedEvaluation(pid, empId, bandFor(empId));
            _performance.ClosePeriod(pid);
            return pid;
        }

        private void SeedEvaluation(long periodId, long employeeId, decimal band)
        {
            Result<long> created = _performance.CreateEvaluation(periodId, employeeId, null);
            if (created.IsFailure) return;
            EvaluationDetail detail = _performance.GetEvaluationDetail(created.Value);
            if (detail == null) return;

            var scores = detail.Scores.ToList();
            foreach (EvaluationScore line in scores) ScoreLine(line, band);
            Evaluation ev = detail.Evaluation;
            ev.Evaluator = "Direction";
            _performance.SaveEvaluation(ev, scores);
            _performance.CompleteEvaluation(created.Value);
        }

        private static void ScoreLine(EvaluationScore line, decimal band)
        {
            if (line.Category == CriterionCategory.Kpi)
            {
                decimal target = line.KpiTarget ?? 100m;
                line.KpiActual = Math.Round(target * band, 2, MidpointRounding.AwayFromZero);
                return;
            }
            switch (line.ScoreType)
            {
                case ScoreType.Stars5: line.RawValue = Bound(Math.Round(band * 5m, MidpointRounding.AwayFromZero), 1m, 5m); break;
                case ScoreType.Score20: line.RawValue = Bound(Math.Round(band * 20m, MidpointRounding.AwayFromZero), 1m, 20m); break;
                default: line.RawValue = Bound(Math.Round(band * 100m, MidpointRounding.AwayFromZero), 0m, 100m); break;
            }
        }

        private static decimal Bound(decimal v, decimal lo, decimal hi) => v < lo ? lo : (v > hi ? hi : v);

        private void CreateBehaviors(long companyId, Dictionary<string, EmpSpec> by, DateTime today)
        {
            void Log(string code, bool positive, int daysAgo, string note) =>
                _performance.LogBehavior(companyId, by[code].Id, positive, note, today.AddDays(-daysAgo));

            Log("bouzid", true, 3, "A livré le module avant l'échéance");
            Log("touati", true, 5, "A dépassé son objectif mensuel de vente");
            Log("benali", true, 8, "A formé deux nouveaux opérateurs");
            Log("haddad", false, 6, "Retard répété en début de semaine");
            Log("amrani", true, 10, "Clôture comptable sans aucune erreur");
            Log("kaddour", false, 12, "Absence non justifiée");
            Log("saadi", true, 9, "Maintenance préventive proactive");
            Log("meziane", true, 4, "Résolution rapide d'un incident critique");
            Log("cherif", false, 7, "Écart d'inventaire au magasin");
            Log("ferhat", true, 11, "Excellent retour d'un client clé");
            Log("gherbi", true, 6, "A digitalisé le suivi des congés");
            Log("medjani", false, 14, "Livraison en retard sur un ticket");
            Log("slimani", true, 2, "A fiabilisé les sauvegardes serveur");
            Log("boudiaf", true, 15, "Zéro incident de sécurité ce mois");
            Log("touati", false, 20, "A oublié de relancer un prospect");
            Log("bouzid", true, 18, "Mentorat de l'équipe junior");
            Log("cherif", false, 25, "Retards de pointage répétés");
        }

        // ===== loans (different repayment stages) ===============================

        private void CreateLoans(Dictionary<string, EmpSpec> by, DateTime today)
        {
            // Partially repaid loan (started 4 months ago).
            DateTime s1 = today.AddMonths(-4);
            long l1 = Must(_loans.Save(new Loan
            {
                EmployeeId = by["amrani"].Id,
                Type = LoanType.Loan,
                Principal = 120000m,
                MonthlyInstallment = 10000m,
                StartYear = s1.Year,
                StartMonth = s1.Month,
                Reason = "Prêt personnel"
            }), "prêt AMRANI");
            for (int k = 0; k < 3; k++)
            {
                DateTime p = s1.AddMonths(k);
                _loans.AddManualRepayment(l1, p.Year, p.Month, 10000m);
            }

            // Just-started advance.
            long l2 = Must(_loans.Save(new Loan
            {
                EmployeeId = by["kaddour"].Id,
                Type = LoanType.Advance,
                Principal = 30000m,
                MonthlyInstallment = 15000m,
                StartYear = today.Year,
                StartMonth = today.Month,
                Reason = "Avance sur salaire"
            }), "avance KADDOUR");
            if (l2 == 0) { /* keep compiler happy */ }

            // Nearly settled loan (started 9 months ago, 9 of 10 instalments paid).
            DateTime s3 = today.AddMonths(-9);
            long l3 = Must(_loans.Save(new Loan
            {
                EmployeeId = by["bouzid"].Id,
                Type = LoanType.Loan,
                Principal = 200000m,
                MonthlyInstallment = 20000m,
                StartYear = s3.Year,
                StartMonth = s3.Month,
                Reason = "Prêt véhicule"
            }), "prêt BOUZID");
            for (int k = 0; k < 9; k++)
            {
                DateTime p = s3.AddMonths(k);
                _loans.AddManualRepayment(l3, p.Year, p.Month, 20000m);
            }
        }

        // ===== assets ===========================================================

        private void CreateAssets(long companyId, Dictionary<string, EmpSpec> by, DateTime today)
        {
            AssignAsset(companyId, "Ordinateur portable Dell Latitude 5540", AssetCategory.Laptop, "DL5540-2023-014", 165000m, by["bouzid"].Id, today.AddMonths(-10));
            AssignAsset(companyId, "Ordinateur portable HP ProBook 450", AssetCategory.Laptop, "HP450-2022-031", 145000m, by["medjani"].Id, today.AddMonths(-8));
            AssignAsset(companyId, "Véhicule utilitaire Renault Kangoo", AssetCategory.Vehicle, "0123-118-16", 2200000m, by["hamadi"].Id, today.AddMonths(-14));
            AssignAsset(companyId, "Téléphone Samsung Galaxy A54", AssetCategory.Phone, "SG-A54-77120", 42000m, by["touati"].Id, today.AddMonths(-6));
            AssignAsset(companyId, "Chariot élévateur Toyota 8FG25", AssetCategory.Tool, "TY-8FG25-004", 3800000m, by["cherif"].Id, today.AddMonths(-20));

            // One unassigned asset flagged for upcoming maintenance.
            long thinkpad = Must(_assets.Save(new Asset
            {
                CompanyId = companyId,
                Name = "Ordinateur portable Lenovo ThinkPad T14",
                Category = AssetCategory.Laptop,
                SerialNumber = "LN-T14-2021-009",
                PurchaseDate = today.AddYears(-3),
                PurchaseValue = 130000m,
                Notes = "Batterie à remplacer — maintenance prévue"
            }), "matériel Lenovo");
            _assets.SetStatus(thinkpad, AssetStatus.UnderRepair);
        }

        private void AssignAsset(long companyId, string name, AssetCategory category, string serial, decimal value, long employeeId, DateTime since)
        {
            long id = Must(_assets.Save(new Asset
            {
                CompanyId = companyId,
                Name = name,
                Category = category,
                SerialNumber = serial,
                PurchaseDate = since.AddMonths(-1),
                PurchaseValue = value
            }), "matériel " + name);

            _assets.Assign(id, employeeId, since, "Bon état", "Affectation de démonstration");
        }

        // ===== training (a completed course with certificates) ==================

        private void CreateTraining(long companyId, List<EmpSpec> roster, Dictionary<string, EmpSpec> by, DateTime today)
        {
            long security = Must(_training.Save(new TrainingSession
            {
                CompanyId = companyId,
                Title = "Sécurité et prévention des risques",
                Category = "Sécurité",
                Provider = "INPRP",
                Status = TrainingStatus.Completed,
                StartDate = today.AddMonths(-3),
                EndDate = today.AddMonths(-3).AddDays(3),
                Location = "Boufarik",
                Cost = 120000m
            }), "formation sécurité");

            string[] prod = { "boudiaf", "kaddour", "saadi", "haddad", "cherif" };
            int c = 1;
            foreach (string code in prod)
            {
                Result enroll = _training.Enroll(security, by[code].Id);
                if (enroll.IsSuccess)
                {
                    TrainingParticipantSummary participant = _training.GetParticipants(security)
                        .FirstOrDefault(p => p.EmployeeId == by[code].Id);
                    if (participant != null)
                    {
                        _training.SetResult(participant.ParticipantId, TrainingResult.Completed, "Réussi",
                            "CERT-SEC-" + today.Year + "-" + c.ToString("00"));
                    }
                }
                c++;
            }

            // Mark the session finished (Save creates it as Planned).
            _training.SetStatus(security, TrainingStatus.Completed);

            // A completed IT course (rounds out the Informatique team's dossiers).
            long dev = Must(_training.Save(new TrainingSession
            {
                CompanyId = companyId,
                Title = "Développement .NET avancé",
                Category = "Informatique",
                Provider = "INFP Boufarik",
                Status = TrainingStatus.Completed,
                StartDate = today.AddMonths(-5),
                EndDate = today.AddMonths(-5).AddDays(5),
                Location = "Alger",
                Cost = 180000m
            }), "formation développement");

            int di = 1;
            foreach (string code in new[] { "bouzid", "slimani", "medjani" })
            {
                Result enrollDev = _training.Enroll(dev, by[code].Id);
                if (enrollDev.IsSuccess)
                {
                    TrainingParticipantSummary p = _training.GetParticipants(dev).FirstOrDefault(x => x.EmployeeId == by[code].Id);
                    if (p != null)
                    {
                        _training.SetResult(p.ParticipantId, TrainingResult.Completed, "Réussi", "CERT-DEV-" + today.Year + "-" + di.ToString("00"));
                    }
                }
                di++;
            }
            _training.SetStatus(dev, TrainingStatus.Completed);

            // A planned course (to show the pipeline).
            long office = Must(_training.Save(new TrainingSession
            {
                CompanyId = companyId,
                Title = "Bureautique avancée sur Excel",
                Category = "Bureautique",
                Provider = "Centre de formation Blida",
                Status = TrainingStatus.Planned,
                StartDate = today.AddDays(21),
                Location = "Blida",
                Cost = 60000m
            }), "formation bureautique");
            _training.Enroll(office, by["gherbi"].Id);
            _training.Enroll(office, by["lounici"].Id);
        }

        // ===== work certificates ================================================

        private void CreateCertificates(Dictionary<string, EmpSpec> by, DateTime today)
        {
            Must(_certificates.Save(new WorkCertificate
            {
                EmployeeId = by["amrani"].Id,
                Type = CertificateType.WorkCertificate,
                IssueDate = today.AddDays(-12),
                Purpose = "Pour servir et valoir ce que de droit"
            }), "attestation de travail");

            Must(_certificates.Save(new WorkCertificate
            {
                EmployeeId = by["bouzid"].Id,
                Type = CertificateType.SalaryCertificate,
                IssueDate = today.AddDays(-5),
                Purpose = "Demande de crédit bancaire"
            }), "attestation de salaire");
        }

        // ===== helpers ==========================================================

        private static long Must(Result<long> result, string what)
        {
            if (result.IsFailure)
            {
                throw new InvalidOperationException("Données de démo — " + what + " : " + result.Error);
            }
            return result.Value;
        }

        private static void Must(Result result, string what)
        {
            if (result.IsFailure)
            {
                throw new InvalidOperationException("Données de démo — " + what + " : " + result.Error);
            }
        }

        /// <summary>Draws a clean navy/teal monogram logo (a real-looking placeholder, not a box).</summary>
        private static byte[] BuildLogo()
        {
            using (var bmp = new Bitmap(180, 180))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                var body = new Rectangle(10, 10, 160, 160);
                using (var path = RoundedRect(body, 34))
                using (var navy = new SolidBrush(Color.FromArgb(0x1B, 0x2A, 0x4A)))
                using (var teal = new SolidBrush(Color.FromArgb(0x0F, 0x9B, 0x8E)))
                using (var white = new SolidBrush(Color.White))
                {
                    g.FillPath(navy, path);
                    g.FillEllipse(teal, 112, 112, 60, 60);
                    using (var font = new Font("Segoe UI", 64f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.DrawString("AI", font, white, new RectangleF(10, 6, 160, 160), sf);
                    }
                }

                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Mutable spec for one demo employee.</summary>
        private sealed class EmpSpec
        {
            public EmpSpec(string code, string lastFr, string firstFr, string lastAr, string firstAr,
                Gender gender, string dept, string poste, decimal salary, MaritalStatus marital,
                int dependents, int hireYear, ContractType contract, bool isManager)
            {
                Code = code; LastFr = lastFr; FirstFr = firstFr; LastAr = lastAr; FirstAr = firstAr;
                Gender = gender; Dept = dept; Poste = poste; Salary = salary; Marital = marital;
                Dependents = dependents; HireDate = new DateTime(hireYear, 3, 15); Contract = contract; IsManager = isManager;
            }

            // Probation new-hire overload (hired ~2 months ago).
            public EmpSpec(string code, string lastFr, string firstFr, string lastAr, string firstAr,
                Gender gender, string dept, string poste, decimal salary, MaritalStatus marital,
                int dependents, bool today)
            {
                Code = code; LastFr = lastFr; FirstFr = firstFr; LastAr = lastAr; FirstAr = firstAr;
                Gender = gender; Dept = dept; Poste = poste; Salary = salary; Marital = marital;
                Dependents = dependents; HireDate = DateTime.Today.AddDays(-60); Contract = ContractType.Cdd; IsManager = false;
            }

            public string Code { get; }
            public string LastFr { get; }
            public string FirstFr { get; }
            public string LastAr { get; }
            public string FirstAr { get; }
            public Gender Gender { get; }
            public string Dept { get; }
            public string Poste { get; }
            public decimal Salary { get; }
            public MaritalStatus Marital { get; }
            public int Dependents { get; }
            public DateTime HireDate { get; }
            public ContractType Contract { get; }
            public bool IsManager { get; }
            public long Id { get; set; }
        }
    }
}
