using System;
using System.Data.SQLite;
using System.IO;
using NUnit.Framework;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 2 — silent-overwrite prevention. Simulates two users who loaded the same record and
    /// proves the SECOND save is REJECTED with the concurrency code instead of overwriting the
    /// first user's change (which the old blind "WHERE Id=@Id" did silently). Also proves a normal
    /// single-user edit-then-edit sequence still works (no false conflicts).
    /// Evidence by execution against a real SQLite database + the real service/repository stack.
    /// </summary>
    [TestFixture]
    public sealed class OptimisticLockingTests
    {
        private string _dir;
        private SqliteConnectionFactory _factory;
        private UnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private LoanService _loans;
        private ContractService _contracts;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-oclock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(_factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _loans = new LoanService(_uow);
            _contracts = new ContractService(_uow);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private long NewCompanyAndEmployee(out long companyId)
        {
            companyId = _companies.Create(new Company { NameFr = "SARL Test", Nif = "000000000000000" }).Value;
            Result<long> emp = _employees.Create(new Employee
            {
                CompanyId = companyId,
                LastNameFr = "Benali",
                FirstNameFr = "Karim",
                Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi,
                HireDate = DateTime.Today.AddYears(-2),
                BaseSalary = 50000m,
                IsActive = true
            });
            Assert.That(emp.IsSuccess, Is.True, "employee create should succeed");
            return emp.Value;
        }

        [Test]
        public void Employee_SecondSave_IsRejected_NotOverwritten()
        {
            long empId = NewCompanyAndEmployee(out _);

            // Two users open the SAME employee.
            Employee userA = _employees.Get(empId);
            Employee userB = _employees.Get(empId);
            Assert.That(userA.RowVersion, Is.EqualTo(userB.RowVersion), "both loaded the same version");

            // User A changes the base salary and saves first.
            userA.BaseSalary = 60000m;
            Result a = _employees.Update(userA);
            Assert.That(a.IsSuccess, Is.True, "first save must succeed");

            // User B (stale) changes the first name and saves second → must be REJECTED.
            userB.FirstNameFr = "Samir";
            Result b = _employees.Update(userB);
            Assert.That(b.IsFailure, Is.True, "second save must be rejected, not silently applied");
            Assert.That(b.ErrorCode, Is.EqualTo(ErrorCodes.Concurrency));

            // The database still holds user A's change, and user B's overwrite did NOT land.
            Employee fresh = _employees.Get(empId);
            Assert.That(fresh.BaseSalary, Is.EqualTo(60000m), "user A's change survived");
            Assert.That(fresh.FirstNameFr, Is.EqualTo("Karim"), "user B did NOT overwrite the name");
        }

        [Test]
        public void Employee_SequentialEdits_StillWork_NoFalseConflict()
        {
            long empId = NewCompanyAndEmployee(out _);

            Employee e = _employees.Get(empId);
            e.BaseSalary = 55000m;
            Assert.That(_employees.Update(e).IsSuccess, Is.True, "first edit");

            // Re-open (fresh version) and edit again — must succeed, no false conflict.
            Employee again = _employees.Get(empId);
            again.Poste = "Chef d'atelier";
            Assert.That(_employees.Update(again).IsSuccess, Is.True, "second sequential edit");

            Assert.That(_employees.Get(empId).Poste, Is.EqualTo("Chef d'atelier"));
        }

        [Test]
        public void Loan_SecondSave_IsRejected()
        {
            long empId = NewCompanyAndEmployee(out _);
            long loanId = _loans.Save(new Loan
            {
                EmployeeId = empId,
                Type = LoanType.Loan,
                Principal = 100000m,
                MonthlyInstallment = 10000m,
                StartYear = DateTime.Today.Year,
                StartMonth = 1
            }).Value;

            Loan a = _loans.Get(loanId);
            Loan b = _loans.Get(loanId);

            a.Notes = "edited by A";
            Assert.That(_loans.Save(a).IsSuccess, Is.True);

            b.Notes = "edited by B";
            Result<long> second = _loans.Save(b);
            Assert.That(second.IsFailure, Is.True);
            Assert.That(second.ErrorCode, Is.EqualTo(ErrorCodes.Concurrency));
            Assert.That(_loans.Get(loanId).Notes, Is.EqualTo("edited by A"));
        }

        [Test]
        public void Company_SecondSave_IsRejected()
        {
            long id = _companies.Create(new Company { NameFr = "SARL One", Nif = "000000000000000" }).Value;
            Company a = _companies.Get(id);
            Company b = _companies.Get(id);

            a.NameFr = "SARL One (A)";
            Assert.That(_companies.Update(a).IsSuccess, Is.True);

            b.NameFr = "SARL One (B)";
            Result second = _companies.Update(b);
            Assert.That(second.IsFailure, Is.True);
            Assert.That(second.ErrorCode, Is.EqualTo(ErrorCodes.Concurrency));
            Assert.That(_companies.Get(id).NameFr, Is.EqualTo("SARL One (A)"));
        }

        [Test]
        public void Contract_SecondSave_IsRejected()
        {
            long empId = NewCompanyAndEmployee(out _);
            long contractId = _contracts.Save(new EmploymentContract
            {
                EmployeeId = empId,
                Type = ContractType.Cdi,
                BaseSalary = 50000m,
                StartDate = DateTime.Today.AddYears(-1)
            }).Value;

            EmploymentContract a = _contracts.Get(contractId);
            EmploymentContract b = _contracts.Get(contractId);

            a.Notes = "A";
            Assert.That(_contracts.Save(a).IsSuccess, Is.True);

            b.Notes = "B";
            Result<long> second = _contracts.Save(b);
            Assert.That(second.IsFailure, Is.True);
            Assert.That(second.ErrorCode, Is.EqualTo(ErrorCodes.Concurrency));
        }
    }
}
