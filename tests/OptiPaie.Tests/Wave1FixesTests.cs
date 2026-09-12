using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Wave 1 (money &amp; data loss) — non-regression tests against a real SQLite database.
    /// Each test fails if the corresponding defect returns.
    /// </summary>
    [TestFixture]
    public sealed class Wave1FixesTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uowf;
        private ICompanyService _companies;
        private IEmployeeService _employees;
        private ILoanService _loans;
        private IAttendanceService _attendance;
        private long _companyId;
        private long _employeeId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-wave1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uowf = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uowf, new CompanyValidator());
            _employees = new EmployeeService(_uowf, new EmployeeValidator());
            _loans = new LoanService(_uowf);
            _attendance = new AttendanceService(_uowf);

            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId, LastNameFr = "BENALI", FirstNameFr = "Karim",
                    Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi, BaseSalary = 40000m, HireDate = new DateTime(2020, 1, 1), IsActive = true
                });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ── B2 : deleting a company that still has employees is refused ──
        [Test]
        public void DeletingACompanyWithEmployees_IsRefused()
        {
            Result result = _companies.Delete(_companyId);
            Assert.That(result.IsFailure, Is.True, "a company holding employees must not be deletable");
            Assert.That(_companies.Get(_companyId), Is.Not.Null, "the company (and its file) still exists");
        }

        [Test]
        public void DeletingAnEmptyCompany_Succeeds()
        {
            long empty = _companies.Create(new Company { NameFr = "SARL Vide", Nif = "111111111111111" }).Value;
            Assert.That(_companies.Delete(empty).IsSuccess, Is.True);
        }

        // ── E2a : deleting an employee who has payslips is refused ──
        [Test]
        public void DeletingAnEmployeeWithPayslips_IsRefused()
        {
            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                long runId = uow.PayrollRuns.Insert(new PayrollRun
                {
                    CompanyId = _companyId, PeriodYear = 2026, PeriodMonth = 3,
                    RunStatus = RunStatus.Archived, GeneratedAtUtc = new DateTime(2026, 3, 31)
                });
                uow.Payslips.Insert(new Payslip { RunId = runId, EmployeeId = _employeeId, SalaireBrut = 40000m, NetSalaire = 34600m });
                uow.Commit();
            }

            Result result = _employees.Delete(_employeeId);
            Assert.That(result.IsFailure, Is.True, "an employee with payslip/CNAS history must not be deletable");
            Assert.That(_employees.Get(_employeeId), Is.Not.Null);
        }

        // ── C2 : the loan is credited EXACTLY the amount withheld on the payslip ──
        [Test]
        public void LoanRecovery_CreditsExactlyTheWithheldAmount_NotTheInstalment()
        {
            _loans.Save(new Loan
            {
                EmployeeId = _employeeId, Type = LoanType.Loan, Principal = 100000m, MonthlyInstallment = 10000m,
                StartYear = 2026, StartMonth = 1, Status = LoanStatus.Active
            });

            // The accountant reduced the recovery line to 3 000 on the payslip.
            Result<decimal> recorded = _loans.RecordPayrollDeductions(_employeeId, 2026, 3, 3000m);
            Assert.That(recorded.Value, Is.EqualTo(3000m), "the loan must be credited what was withheld, not the 10 000 instalment");
            Assert.That(_loans.GetOutstanding(_employeeId), Is.EqualTo(97000m));

            // A zeroed line credits nothing.
            Result<decimal> none = _loans.RecordPayrollDeductions(_employeeId, 2026, 4, 0m);
            Assert.That(none.Value, Is.EqualTo(0m));
            Assert.That(_loans.GetOutstanding(_employeeId), Is.EqualTo(97000m), "a zero withholding leaves the balance untouched");
        }

        // ── D : a manual matrix entry detaches the day from a synced leave (clears the "[Congé]" marker) ──
        [Test]
        public void ManualAttendanceEntry_ClearsTheLeaveMarker()
        {
            DateTime day = DateTime.Today.AddDays(-3);
            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                uow.Attendance.Insert(new AttendanceRecord
                {
                    EmployeeId = _employeeId, WorkDate = day, Status = AttendanceStatus.Absent, Notes = "[Congé] Congé annuel"
                });
                uow.Commit();
            }

            Assert.That(_attendance.SetDayStatus(_employeeId, day, AttendanceStatus.Present).IsSuccess, Is.True);

            using (IUnitOfWork uow = _uowf.Create())
            {
                AttendanceRecord rec = uow.Attendance.GetByEmployeeAndDate(_employeeId, day);
                Assert.That(rec.Status, Is.EqualTo(AttendanceStatus.Present));
                Assert.That(rec.Notes, Is.Null, "the [Congé] marker is cleared so cancelling the leave never deletes this day");
            }
        }
    }
}
