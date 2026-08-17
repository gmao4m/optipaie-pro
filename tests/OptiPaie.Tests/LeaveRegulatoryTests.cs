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

namespace OptiPaie.Tests
{
    /// <summary>
    /// Regulatory proofs for the Congés module (loi 90-11 / loi 83-11 research): accrual excluding
    /// unpaid, the day count excluding week-end AND holidays, the three payment categories mapping to
    /// the expected attendance effect, the reliquat (solde de tout compte), and the live preview. Every
    /// legal option is a company setting that DEFAULTS to the historical behaviour.
    /// </summary>
    [TestFixture]
    public sealed class LeaveRegulatoryTests
    {
        private static readonly int Year = DateTime.Today.Year - 1;

        private string _directory;
        private IUnitOfWorkFactory _uow;
        private LeaveService _leave;
        private IAttendanceService _attendance;

        private long _companyId;
        private long _employeeId;
        private DateTime _weekStart;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-leavereg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _leave = new LeaveService(_uow);
            _attendance = new AttendanceService(_uow);

            using (IUnitOfWork uow = _uow.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Régl", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId, LastNameFr = "BENALI", FirstNameFr = "Karim",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, HireDate = new DateTime(Year - 4, 1, 1), BaseSalary = 40000m, IsActive = true
                });
                uow.Commit();
            }

            _weekStart = FirstDayOfWeek(Year, 6, DayOfWeek.Sunday);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        private static DateTime FirstDayOfWeek(int year, int month, DayOfWeek dow)
        {
            var day = new DateTime(year, month, 1);
            while (day.DayOfWeek != dow) day = day.AddDays(1);
            return day;
        }

        private LeaveRequest Req(LeaveType type, DateTime start, DateTime end, long? typeId = null)
        {
            return new LeaveRequest { EmployeeId = _employeeId, Type = type, LeaveTypeId = typeId, StartDate = start, EndDate = end, Reason = "Test" };
        }

        private long InsertType(string code, LeaveType baseType, PaymentCategory cat, bool decrements)
        {
            using (IUnitOfWork uow = _uow.Create())
            {
                uow.BeginTransaction();
                long id = uow.LeaveTypes.Insert(new LeaveTypeDefinition
                {
                    CompanyId = _companyId, Code = code, LabelAr = code, LabelFr = code,
                    BaseType = baseType, PaymentCategory = cat, DecrementsAnnualBalance = decrements, IsActive = true
                });
                uow.Commit();
                return id;
            }
        }

        private void InsertHoliday(DateTime date, string name)
        {
            using (IUnitOfWork uow = _uow.Create())
            {
                uow.BeginTransaction();
                uow.Holidays.Insert(new Holiday { CompanyId = _companyId, HolidayDate = date, NameAr = name, IsReligious = true });
                uow.Commit();
            }
        }

        private void SetFlags(Action<LeaveSettings> configure)
        {
            LeaveSettings s = _leave.GetSettings(_companyId);
            configure(s);
            Assert.That(_leave.SaveSettings(_companyId, s).IsSuccess, Is.True);
        }

        // ============================================================ PREUVE 3 — acquisition

        [Test]
        public void Accrual_FullYear_Gives30_Capped()
        {
            LeaveBalance b = _leave.GetBalance(_employeeId, Year);
            Assert.That(b.Entitlement, Is.EqualTo(30m), "2,5 j/mois × 12 = 30, plafonné");
        }

        [Test]
        public void Accrual_ExcludesUnpaidDominatedMonths_WhenEnabled()
        {
            SetFlags(s => s.AccrualExcludesUnpaid = true);

            // A full month of approved unpaid leave (March) → that month accrues nothing.
            long id = _leave.Save(Req(LeaveType.Unpaid, new DateTime(Year, 3, 1), new DateTime(Year, 3, 31))).Value;
            _leave.Approve(id, null);

            LeaveBalance b = _leave.GetBalance(_employeeId, Year);
            Assert.That(b.Entitlement, Is.EqualTo(27.5m), "11 mois × 2,5 (mars, dominé par le sans-solde, n'accorde rien)");

            var detail = _leave.GetAccrualDetail(_employeeId, Year);
            Assert.That(detail.Single(m => m.Month == 3).Accrued, Is.EqualTo(0m), "mars n'accorde pas de droit");
            Assert.That(detail.Single(m => m.Month == 4).Accrued, Is.EqualTo(2.5m), "les autres mois accordent 2,5");
        }

        // ============================================================ PREUVE 4 — décompte (week-end + fériés)

        [Test]
        public void Count_ExcludesWeekendThenAlsoHoliday_WhenEnabled()
        {
            DateTime from = _weekStart.AddDays(1);   // Monday
            DateTime to = _weekStart.AddDays(4);     // Thursday → 4 working days (week-end already excluded)
            DateTime holiday = _weekStart.AddDays(2); // Tuesday

            // Default: holidays NOT excluded → 4 working days.
            Assert.That(_leave.Preview(Req(LeaveType.Annual, from, to)).Days, Is.EqualTo(4m),
                "week-end exclu, férié non exclu → 4");

            // Enable holiday exclusion + a stored holiday on the Tuesday → one fewer counted day.
            SetFlags(s => s.ExcludeHolidays = true);
            InsertHoliday(holiday, "عيد تجريبي");

            Assert.That(_leave.Preview(Req(LeaveType.Annual, from, to)).Days, Is.EqualTo(3m),
                "week-end ET férié exclus → 3");
        }

        [Test]
        public void Count_ExcludesFixedCivilHoliday_WhenEnabled()
        {
            SetFlags(s => s.ExcludeHolidays = true);

            // Pick a fixed civil holiday that falls on a working day this year.
            DateTime[] civil = { new DateTime(Year, 1, 1), new DateTime(Year, 1, 12), new DateTime(Year, 5, 1), new DateTime(Year, 7, 5), new DateTime(Year, 11, 1) };
            DateTime working = civil.First(d => d.DayOfWeek != DayOfWeek.Friday && d.DayOfWeek != DayOfWeek.Saturday);

            LeavePreview p = _leave.Preview(Req(LeaveType.Annual, working, working));
            Assert.That(p.Days, Is.EqualTo(0m), "un jour férié civil fixe n'est pas décompté");
        }

        // ============================================================ PREUVE 5 — catégorie → présence

        [Test]
        public void Category_CnasPaid_WritesLeaveByDefault_UnpaidWritesAbsent()
        {
            long cnas = InsertType("TCNAS", LeaveType.Sick, PaymentCategory.SocialSecurity, decrements: false);
            long unpaid = InsertType("TUNPAID", LeaveType.Unpaid, PaymentCategory.Unpaid, decrements: false);

            long a = _leave.Save(Req(LeaveType.Sick, _weekStart, _weekStart, cnas)).Value;
            _leave.Approve(a, null);
            Assert.That(_attendance.Get(_employeeId, _weekStart).Status, Is.EqualTo(AttendanceStatus.Leave),
                "CNAS payé → 'Congé' par défaut (salaire maintenu, aucun montant ne bouge)");

            long b = _leave.Save(Req(LeaveType.Unpaid, _weekStart.AddDays(7), _weekStart.AddDays(7), unpaid)).Value;
            _leave.Approve(b, null);
            Assert.That(_attendance.Get(_employeeId, _weekStart.AddDays(7)).Status, Is.EqualTo(AttendanceStatus.Absent),
                "sans solde → 'Absent' (décompté par la paie)");
        }

        [Test]
        public void Category_StrictCnas_WritesAbsent_WhenEnabled()
        {
            long cnas = InsertType("TCNAS2", LeaveType.Sick, PaymentCategory.SocialSecurity, decrements: false);
            SetFlags(s => s.StrictCnasTreatment = true);

            long a = _leave.Save(Req(LeaveType.Sick, _weekStart, _weekStart, cnas)).Value;
            _leave.Approve(a, null);
            Assert.That(_attendance.Get(_employeeId, _weekStart).Status, Is.EqualTo(AttendanceStatus.Absent),
                "traitement légal strict activé → l'employeur suspend le salaire (Absent)");
        }

        // ============================================================ PREUVE 9 — solde de tout compte

        [Test]
        public void FinalSettlement_ComputesReliquat()
        {
            // Leaving on 30 June → 6 months accrued (15 j), nothing taken → 15 j × (40000/30) = 20000.
            FinalSettlement s = _leave.ComputeFinalSettlement(_employeeId, new DateTime(Year, 6, 30));
            Assert.That(s.Acquired, Is.EqualTo(15m), "6 mois × 2,5");
            Assert.That(s.RemainingDays, Is.EqualTo(15m));
            Assert.That(s.DailyRate, Is.EqualTo(40000m / 30m).Within(0.001m));
            Assert.That(s.Amount, Is.EqualTo(20000m), "reliquat = jours dus × salaire/30");
        }

        // ============================================================ aperçu à la saisie (d)

        [Test]
        public void Preview_ShowsDays_Category_BalanceImpact_AndReason()
        {
            LeavePreview ok = _leave.Preview(Req(LeaveType.Annual, _weekStart, _weekStart.AddDays(4)));
            Assert.That(ok.Ok, Is.True);
            Assert.That(ok.Days, Is.EqualTo(5m));
            Assert.That(ok.DecrementsBalance, Is.True, "congé annuel décompte le solde");
            Assert.That(ok.Category, Is.EqualTo(PaymentCategory.EmployerPaid));
            Assert.That(ok.AvailableAfter, Is.EqualTo(ok.AvailableBefore - 5m), "solde après = avant − jours");

            // Insufficient balance → blocked with the precise reason.
            SetFlagsCap3();
            LeavePreview tooMuch = _leave.Preview(Req(LeaveType.Annual, _weekStart, _weekStart.AddDays(4)));
            Assert.That(tooMuch.Ok, Is.False);
            Assert.That(tooMuch.ReasonCode, Is.EqualTo("Leave_InsufficientBalance"));
            Assert.That(tooMuch.Reason, Is.Not.Null.And.Not.Empty, "le motif est explicite");
        }

        private void SetFlagsCap3()
        {
            LeaveSettings s = _leave.GetSettings(_companyId);
            s.DaysPerMonth = 0.25m; s.AnnualCap = 3m;
            Assert.That(_leave.SaveSettings(_companyId, s).IsSuccess, Is.True);
        }
    }
}
