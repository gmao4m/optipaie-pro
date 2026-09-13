using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Verifies EVERY figure on the redesigned dashboard against a fixture with known expected
    /// values (the consolidated <see cref="IDashboardService.BuildOverview"/>), the SQL aggregates
    /// against the service definitions they replace (drift guard), the expiring-contract fix, the
    /// distribution invariants and the empty/edge states. A dashboard that shows a wrong number is
    /// worse than no dashboard.
    /// </summary>
    [TestFixture]
    public sealed class DashboardOverviewTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uowf;
        private CompanyService _companies;
        private EmployeeService _employees;
        private ContractService _contracts;
        private LeaveService _leave;
        private LoanService _loans;
        private AttendanceService _attendance;
        private AtsService _ats;
        private AssetService _assets;
        private TrainingService _training;
        private DashboardService _dashboard;

        private readonly DateTime _today = DateTime.Today;
        private long _companyId;
        private long _e1, _e2, _e3;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-ov-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uowf = new UnitOfWorkFactory(factory);

            _companies = new CompanyService(_uowf, new CompanyValidator());
            _employees = new EmployeeService(_uowf, new EmployeeValidator());
            _contracts = new ContractService(_uowf);
            _leave = new LeaveService(_uowf);
            _loans = new LoanService(_uowf);
            _attendance = new AttendanceService(_uowf);
            _ats = new AtsService(_uowf, new EmployeeValidator());
            _assets = new AssetService(_uowf);
            _training = new TrainingService(_uowf);
            _dashboard = new DashboardService(_companies, _employees, _contracts, _leave, _loans, _attendance, _ats, _assets, _training);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private long Emp(long companyId, Gender g, DateTime? birth, string category, ContractType ct,
            string dept, string poste, DateTime hire, DateTime? exit, bool active, decimal salary, string last = "N", string first = "P")
        {
            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                long id = uow.Employees.Insert(new Employee
                {
                    CompanyId = companyId, LastNameFr = last, FirstNameFr = first, Gender = g, BirthDate = birth,
                    Category = category, ContractType = ct, MaritalStatus = MaritalStatus.Single, Department = dept,
                    Poste = poste, HireDate = hire, ExitDate = exit, PaymentMode = PaymentMode.Cash, BaseSalary = salary, IsActive = active
                });
                uow.Commit();
                return id;
            }
        }

        private void SeedRich()
        {
            _companyId = _companies.Create(new Company { NameFr = "SARL Test", Nif = "000000000000000" }).Value;

            _e1 = Emp(_companyId, Gender.Male, _today.AddYears(-30), "Cadre", ContractType.Cdi, "Production", "Chef", _today.AddYears(-6), null, true, 60000m, "BENALI", "Karim");
            _e2 = Emp(_companyId, Gender.Female, _today.AddYears(-40), "Maîtrise", ContractType.Cdd, "Ventes", "Commercial", _today.AddDays(-15), null, true, 40000m);
            _e3 = Emp(_companyId, Gender.Male, null, "", ContractType.Cdi, "", "Ouvrier", _today.AddYears(-2), null, true, 50000m);
            // A leaver whose exit falls inside the last-30-days period.
            Emp(_companyId, Gender.Female, _today.AddYears(-50), "Cadre", ContractType.Cdd, "Ventes", "Commercial", _today.AddYears(-3), _today.AddDays(-10), false, 45000m);

            // e1: active contract ending in 15 days → the one upcoming deadline.
            long c1 = _contracts.Save(new EmploymentContract
            {
                EmployeeId = _e1, Type = ContractType.Cdd, BaseSalary = 60000m, Position = "Chef",
                StartDate = _today.AddMonths(-6), EndDate = _today.AddDays(15), TrialPeriodDays = 0
            }).Value;
            _contracts.Activate(c1);

            // Pending leave for e1.
            _leave.Save(new LeaveRequest { EmployeeId = _e1, Type = LeaveType.Annual, StartDate = NextSunday(), EndDate = NextSunday().AddDays(2) });

            // One active loan for e1: principal 100000, 30000 repaid → 70000 outstanding.
            long loan = _loans.Save(new Loan
            {
                EmployeeId = _e1, Type = LoanType.Loan, Principal = 100000m, MonthlyInstallment = 10000m,
                StartYear = _today.Year, StartMonth = _today.Month
            }).Value;
            _loans.AddManualRepayment(loan, _today.Year, _today.Month, 30000m);

            // Two assets, one assigned.
            long a1 = _assets.Save(new Asset { CompanyId = _companyId, Name = "PC1", Category = AssetCategory.Laptop, PurchaseValue = 80000m }).Value;
            _assets.Save(new Asset { CompanyId = _companyId, Name = "PC2", Category = AssetCategory.Laptop, PurchaseValue = 80000m });
            _assets.Assign(a1, _e1, _today, "Neuf", null);

            // One planned training.
            _training.Save(new TrainingSession { CompanyId = _companyId, Title = "Sécurité", StartDate = _today.AddDays(7) });

            // Two open postings: 2 + 1 candidates = 3 candidates total.
            long p1 = _ats.SavePosting(new JobPosting { CompanyId = _companyId, Title = "Magasinier", Department = "Logistique", OpenDate = _today, Positions = 1 }).Value;
            long p2 = _ats.SavePosting(new JobPosting { CompanyId = _companyId, Title = "Chauffeur", Department = "Transport", OpenDate = _today, Positions = 1 }).Value;
            _ats.SaveCandidate(new Candidate { PostingId = p1, LastName = "A", FirstName = "A", Phone = "0555000001" });
            _ats.SaveCandidate(new Candidate { PostingId = p1, LastName = "B", FirstName = "B", Phone = "0555000002" });
            _ats.SaveCandidate(new Candidate { PostingId = p2, LastName = "C", FirstName = "C", Phone = "0555000003" });

            // Attendance today: e1 present, e2 mission, e3 on leave.
            _attendance.SetDayStatus(_e1, _today, AttendanceStatus.Present);
            _attendance.SetDayStatus(_e2, _today, AttendanceStatus.Mission);
            _attendance.SetDayStatus(_e3, _today, AttendanceStatus.Leave);
        }

        private DashboardOverview Build() => _dashboard.BuildOverview(_companyId, _today.AddDays(-30), _today, 30);

        private static DateTime NextSunday()
        {
            var d = DateTime.Today.AddDays(7);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }

        // ──────────────────────────────────────── headline figures

        [Test]
        public void Payroll_MasseAndAverage_AreTheActiveBaseSalaries()
        {
            SeedRich();
            DashboardOverview o = Build();
            Assert.That(o.MasseSalariale, Is.EqualTo(150000m), "60000 + 40000 + 50000 (leaver excluded)");
            Assert.That(o.SalaireMoyen, Is.EqualTo(50000m), "150000 / 3");
            Assert.That(o.MasseTrend.Count, Is.EqualTo(6), "six months of trend");
        }

        [Test]
        public void Headcount_AverageAge_Turnover_MatchTheRoster()
        {
            SeedRich();
            DashboardOverview o = Build();
            Assert.That(o.Workforce.Headcount, Is.EqualTo(3));
            Assert.That(o.Workforce.AverageAge, Is.EqualTo(35m), "(30 + 40) / 2, e3 has no birth date");
            Assert.That(o.Workforce.AgeKnownCount, Is.EqualTo(2));
            Assert.That(o.Workforce.Entries, Is.EqualTo(1), "e2 hired 15 days ago");
            Assert.That(o.Workforce.Exits, Is.EqualTo(1), "the leaver exited 10 days ago");
            Assert.That(o.Workforce.TurnoverRate, Is.EqualTo(33.3m), "1 / 3 avg headcount × 100");
        }

        [Test]
        public void AttendanceToday_IsMutuallyExclusive_AndSums()
        {
            SeedRich();
            DashboardOverview o = Build();
            Assert.That(o.PresentToday, Is.EqualTo(2), "present + mission");
            Assert.That(o.OnMissionToday, Is.EqualTo(1));
            Assert.That(o.OnLeaveToday, Is.EqualTo(1));
            Assert.That(o.PresentToday + o.OnLeaveToday, Is.EqualTo(o.Workforce.Headcount), "present and on-leave partition today's roster");
        }

        [Test]
        public void ModuleKpis_MatchTheSeededData()
        {
            SeedRich();
            DashboardOverview o = Build();
            Assert.That(o.ActiveContracts, Is.EqualTo(1));
            Assert.That(o.ContractsExpiringSoon, Is.EqualTo(1));
            Assert.That(o.PendingLeave, Is.EqualTo(1));
            Assert.That(o.ActiveLoans, Is.EqualTo(1));
            Assert.That(o.LoanOutstanding, Is.EqualTo(70000m), "100000 principal − 30000 repaid");
            Assert.That(o.AssetsAssigned, Is.EqualTo(1));
            Assert.That(o.TrainingUpcoming, Is.EqualTo(1));
            Assert.That(o.OpenPostings, Is.EqualTo(2));
            Assert.That(o.Candidates, Is.EqualTo(3));
        }

        [Test]
        public void Queues_CarryRawEmployeeNamesForLocalisation()
        {
            SeedRich();
            DashboardOverview o = Build();
            Assert.That(o.Deadlines.Count, Is.EqualTo(1));
            Assert.That(o.Deadlines[0].Kind, Is.EqualTo("contract"));
            Assert.That(o.Deadlines[0].EmployeeName, Does.Contain("BENALI"));
            Assert.That(o.Deadlines[0].DaysLeft, Is.InRange(0, 30));
            Assert.That(o.Approvals.Count, Is.EqualTo(1));
            Assert.That(o.Approvals[0].Kind, Is.EqualTo("leave"));
            Assert.That(o.Approvals[0].EmployeeName, Does.Contain("BENALI"));
            Assert.That(o.Approvals[0].StartDate, Is.Not.Null, "the leave range is carried for the localized detail line");
        }

        // ──────────────────────────────────────── the 547-day bug

        [Test]
        public void OverdueContract_IsNotAnUpcomingDeadline()
        {
            _companyId = _companies.Create(new Company { NameFr = "SARL X", Nif = "000000000000000" }).Value;
            long emp = Emp(_companyId, Gender.Male, _today.AddYears(-35), "Cadre", ContractType.Cdd, "Production", "Chef", _today.AddYears(-3), null, true, 50000m, "OVERDUE", "Case");

            // A standalone active CDD whose end date passed 500 days ago (the exact 1.38.x symptom).
            long c = _contracts.Save(new EmploymentContract
            {
                EmployeeId = emp, Type = ContractType.Cdd, BaseSalary = 50000m, Position = "Chef",
                StartDate = _today.AddDays(-900), EndDate = _today.AddDays(-500), TrialPeriodDays = 0
            }).Value;
            _contracts.Activate(c);

            DashboardOverview o = _dashboard.BuildOverview(_companyId, _today.AddDays(-30), _today, 30);
            Assert.That(o.ContractsExpiringSoon, Is.EqualTo(0), "a contract 500 days overdue is not 'expiring soon'");
            Assert.That(o.Deadlines.Any(d => d.EmployeeName != null && d.EmployeeName.Contains("OVERDUE")), Is.False,
                "the overdue contract must never appear as an upcoming deadline");
        }

        // ──────────────────────────────────────── distribution invariants

        [Test]
        public void EveryDistribution_BucketCountsSumToTheTotal_SoPercentagesMakeHundred()
        {
            SeedRich();
            WorkforceAnalytics w = Build().Workforce;
            foreach (WorkforceDistribution d in new[] { w.ByContract, w.ByGender, w.ByMaritalStatus, w.ByCategory, w.ByDepartment, w.ByPoste, w.ByAgeBand, w.BySeniority })
            {
                Assert.That(d.Buckets.Sum(b => b.Count), Is.EqualTo(d.Total),
                    "bucket counts must sum to the total (so their percentages sum to 100)");
            }
        }

        // ──────────────────────────────────────── SQL-aggregate drift guards

        [Test]
        public void Aggregates_EqualTheServiceDefinitionsTheyReplace()
        {
            SeedRich();

            LoanPortfolio lp = _loans.GetActivePortfolio(_companyId);
            var activeLoans = _loans.GetByCompany(_companyId).Where(l => l.Status == LoanStatus.Active).ToList();
            Assert.That(lp.ActiveCount, Is.EqualTo(activeLoans.Count), "active-loan count aggregate == service list");
            Assert.That(lp.TotalOutstanding, Is.EqualTo(activeLoans.Sum(l => l.Outstanding)), "outstanding aggregate == service sum");

            RecruitmentCounts rc = _ats.GetRecruitmentCounts(_companyId);
            var postings = _ats.GetPostingsByCompany(_companyId);
            Assert.That(rc.OpenPostings, Is.EqualTo(postings.Count(p => p.Status == JobStatus.Open)), "open-posting aggregate == service");
            Assert.That(rc.Candidates, Is.EqualTo(postings.Sum(p => p.CandidateCount)), "candidate aggregate == service");

            Assert.That(_assets.CountAssigned(_companyId),
                Is.EqualTo(_assets.GetByCompany(_companyId).Count(a => a.Status == AssetStatus.Assigned)), "assigned-asset aggregate == service");
            Assert.That(_training.CountUpcoming(_companyId),
                Is.EqualTo(_training.GetByCompany(_companyId).Count(t => t.Status == TrainingStatus.Planned || t.Status == TrainingStatus.Ongoing)), "upcoming-training aggregate == service");
        }

        // ──────────────────────────────────────── empty & edge states

        [Test]
        public void EmptyCompany_ProducesZeros_AndNeverThrows()
        {
            _companyId = _companies.Create(new Company { NameFr = "SARL Vide", Nif = "000000000000000" }).Value;
            DashboardOverview o = Build();

            Assert.That(o.Workforce.Headcount, Is.EqualTo(0));
            Assert.That(o.MasseSalariale, Is.EqualTo(0m));
            Assert.That(o.SalaireMoyen, Is.EqualTo(0m));
            Assert.That(o.Workforce.AverageAge, Is.Null);
            Assert.That(o.ActiveLoans, Is.EqualTo(0));
            Assert.That(o.LoanOutstanding, Is.EqualTo(0m));
            Assert.That(o.OpenPostings, Is.EqualTo(0));
            Assert.That(o.AssetsAssigned, Is.EqualTo(0));
            Assert.That(o.TrainingUpcoming, Is.EqualTo(0));
            Assert.That(o.Deadlines, Is.Empty);
            Assert.That(o.Approvals, Is.Empty);
            Assert.That(o.MasseTrend.Count, Is.EqualTo(6));
            Assert.That(o.MasseTrend.All(m => m.Amount == 0m), Is.True);
        }

        [Test]
        public void OneEmployee_MissingBirthDateAndDepartment_IsHandledGracefully()
        {
            _companyId = _companies.Create(new Company { NameFr = "SARL Un", Nif = "000000000000000" }).Value;
            Emp(_companyId, Gender.Male, null, "", ContractType.Cdi, "", "", _today.AddYears(-1), null, true, 55000m);

            DashboardOverview o = Build();
            Assert.That(o.Workforce.Headcount, Is.EqualTo(1));
            Assert.That(o.MasseSalariale, Is.EqualTo(55000m));
            Assert.That(o.SalaireMoyen, Is.EqualTo(55000m));
            Assert.That(o.Workforce.AverageAge, Is.Null, "no birth date → no average, never a crash");
            Assert.That(o.Workforce.ByDepartment.Buckets.Any(b => b.IsUnknown), Is.True, "the blank department goes to « non renseigné »");
            Assert.That(o.Workforce.ByDepartment.Buckets.Sum(b => b.Count), Is.EqualTo(1));
        }

        // ──────────────────────────────────────── retirement indicator

        private long SeedRetirementCompany()
        {
            long c = _companies.Create(new Company { NameFr = "SARL Retraite", Nif = "000000000000000" }).Value;
            // A: man, passed 60 (~1 month ago)
            Emp(c, Gender.Male, _today.AddYears(-60).AddMonths(-1), "Cadre", ContractType.Cdi, "P", "Chef", _today.AddYears(-10), null, true, 50000m, "APASSED");
            // B: man, reaches 60 in ~6 months
            Emp(c, Gender.Male, _today.AddYears(-59).AddMonths(-6), "Cadre", ContractType.Cdi, "P", "Chef", _today.AddYears(-10), null, true, 50000m, "BSOON");
            // C: woman, reaches 55 in ~3 months
            Emp(c, Gender.Female, _today.AddYears(-54).AddMonths(-9), "Cadre", ContractType.Cdi, "P", "Chef", _today.AddYears(-10), null, true, 50000m, "CSOON");
            // D: man, age 30 — far from retirement
            Emp(c, Gender.Male, _today.AddYears(-30), "Cadre", ContractType.Cdi, "P", "Chef", _today.AddYears(-2), null, true, 50000m, "DYOUNG");
            // E: no birth date — excluded (never silently omitted)
            Emp(c, Gender.Male, null, "Cadre", ContractType.Cdi, "P", "Chef", _today.AddYears(-2), null, true, 50000m, "ENOBIRTH");
            // F: woman, passed 55 (~1 year ago)
            Emp(c, Gender.Female, _today.AddYears(-56), "Cadre", ContractType.Cdi, "P", "Chef", _today.AddYears(-10), null, true, 50000m, "FPASSED");
            return c;
        }

        [Test]
        public void Retirement_CountsSoonPassedAndUnknown_SortedSoonestFirst()
        {
            long c = SeedRetirementCompany();
            WorkforceAnalytics w = _dashboard.BuildOverview(c, _today.AddDays(-30), _today, 30, RetirementPolicy.Default).Workforce;

            Assert.That(w.RetirementSoonCount, Is.EqualTo(2), "B (man, +6 mois) and C (woman, +3 mois)");
            Assert.That(w.RetirementPassedCount, Is.EqualTo(2), "A (man) and F (woman) are already past the age");
            Assert.That(w.RetirementUnknownCount, Is.EqualTo(1), "E has no birth date — excluded, never silently omitted");
            Assert.That(w.RetirementCandidates.Count, Is.EqualTo(4));
            Assert.That(w.RetirementCandidates.Select(x => x.RetirementDate).ToList(), Is.Ordered, "soonest retirement date first");
            Assert.That(w.RetirementCandidates.First().AlreadyReached, Is.True, "an already-passed employee sorts first");
        }

        [Test]
        public void Retirement_HonoursConfiguredAges()
        {
            long c = SeedRetirementCompany();
            // Raise the male age to 62: B no longer within a year and A no longer past it; the women are unaffected.
            WorkforceAnalytics w = _dashboard.BuildOverview(c, _today.AddDays(-30), _today, 30, new RetirementPolicy(62, 55)).Workforce;
            Assert.That(w.RetirementSoonCount, Is.EqualTo(1), "only C (woman) within a year once men retire at 62");
            Assert.That(w.RetirementPassedCount, Is.EqualTo(1), "only F (woman) still past the age at 62 for men");
        }

        [Test]
        public void Gender_CannotBeUnspecified_EnforcedByTheDatabase()
        {
            // The spec asks for a "gender unspecified" count. There is none by design: the DB enforces
            // CHECK (Gender IN (1, 2)), so every employee is Male or Female — the unspecified count is
            // structurally always zero.
            long c = _companies.Create(new Company { NameFr = "SARL G", Nif = "000000000000000" }).Value;
            Assert.That(() => Emp(c, (Gender)0, _today.AddYears(-40), "Cadre", ContractType.Cdi, "P", "Chef", _today.AddYears(-10), null, true, 50000m, "NOGENDER"),
                Throws.InstanceOf<Exception>(), "an unset gender is rejected at the database level");
        }

        [Test]
        public void Retirement_EmptyAndSingleUnknown_AreZeroOrExcluded()
        {
            // Empty company.
            long empty = _companies.Create(new Company { NameFr = "SARL Vide", Nif = "000000000000000" }).Value;
            WorkforceAnalytics we = _dashboard.BuildOverview(empty, _today.AddDays(-30), _today, 30, RetirementPolicy.Default).Workforce;
            Assert.That(we.RetirementSoonCount, Is.EqualTo(0));
            Assert.That(we.RetirementPassedCount, Is.EqualTo(0));
            Assert.That(we.RetirementUnknownCount, Is.EqualTo(0));
            Assert.That(we.RetirementCandidates, Is.Empty);

            // One employee, no birth date → excluded, not omitted.
            long one = _companies.Create(new Company { NameFr = "SARL Un", Nif = "000000000000000" }).Value;
            Emp(one, Gender.Male, null, "Cadre", ContractType.Cdi, "P", "Chef", _today.AddYears(-1), null, true, 50000m, "NOBIRTH");
            WorkforceAnalytics wo = _dashboard.BuildOverview(one, _today.AddDays(-30), _today, 30, RetirementPolicy.Default).Workforce;
            Assert.That(wo.RetirementUnknownCount, Is.EqualTo(1));
            Assert.That(wo.RetirementSoonCount, Is.EqualTo(0));
            Assert.That(wo.RetirementPassedCount, Is.EqualTo(0));
        }

        [Test]
        public void Poste_And_Gender_DistributionsAreAvailable_AndSumToHeadcount()
        {
            SeedRich();
            WorkforceAnalytics w = Build().Workforce;
            // Poste distribution present and its percentages sum to 100 (counts sum to total).
            Assert.That(w.ByPoste.Buckets.Sum(b => b.Count), Is.EqualTo(w.ByPoste.Total));
            Assert.That(w.ByPoste.Buckets.Count, Is.GreaterThan(0));
            // Gender is a controlled enum → no "unknown" bucket; every active employee is counted.
            Assert.That(w.ByGender.UnknownCount, Is.EqualTo(0));
            Assert.That(w.ByGender.Buckets.Sum(b => b.Count), Is.EqualTo(w.ByGender.Total));
            Assert.That(w.ByGender.Total, Is.EqualTo(w.Headcount));
        }

        [Test]
        public void BuildOverview_RequiresACompany()
        {
            Assert.That(() => _dashboard.BuildOverview(0, _today.AddDays(-30), _today), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _dashboard.BuildOverview(-1, _today.AddDays(-30), _today), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
