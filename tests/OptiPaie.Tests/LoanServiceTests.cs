using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Loans module — integration tests against a real SQLite file. They prove the
    /// balance is always derived (never stored), that the instalment fed to payroll is
    /// capped at the balance and reproducible, and that a payroll run records the
    /// recovery exactly once per period (idempotent), settling the loan at zero.
    /// </summary>
    [TestFixture]
    public sealed class LoanServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private ILoanService _service;

        private long _companyId;
        private long _employeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-loans-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new LoanService(_unitOfWorkFactory);

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId,
                    LastNameFr = "BENALI",
                    FirstNameFr = "Karim",
                    Gender = Gender.Male,
                    MaritalStatus = MaritalStatus.Single,
                    PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi,
                    HireDate = new DateTime(2020, 1, 1),
                    BaseSalary = 60000m,
                    IsActive = true
                });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        // ---------------------------------------------------------------- validation

        [Test]
        public void Save_ValidLoan_Succeeds()
        {
            Result<long> saved = _service.Save(NewLoan(100000m, 20000m, 2026, 1));

            Assert.That(saved.IsSuccess, Is.True, saved.Error);
            LoanSummary summary = _service.GetSummary(saved.Value);
            Assert.That(summary.Outstanding, Is.EqualTo(100000m));
            Assert.That(summary.RemainingInstallments, Is.EqualTo(5));
        }

        [Test]
        public void Save_InstallmentAbovePrincipal_IsRejected()
        {
            Result<long> saved = _service.Save(NewLoan(50000m, 60000m, 2026, 1));

            Assert.That(saved.IsFailure, Is.True);
        }

        [Test]
        public void Save_NonPositiveAmounts_AreRejected()
        {
            Assert.That(_service.Save(NewLoan(0m, 0m, 2026, 1)).IsFailure, Is.True);
            Assert.That(_service.Save(NewLoan(100000m, 0m, 2026, 1)).IsFailure, Is.True);
        }

        [Test]
        public void Save_UnknownEmployee_IsRejected()
        {
            Loan loan = NewLoan(100000m, 20000m, 2026, 1);
            loan.EmployeeId = 999999;

            Assert.That(_service.Save(loan).IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- deduction schedule

        [Test]
        public void GetMonthlyDeduction_BeforeStartPeriod_IsZero()
        {
            _service.Save(NewLoan(100000m, 20000m, 2026, 3));

            Assert.That(_service.GetMonthlyDeduction(_employeeId, 2026, 2), Is.EqualTo(0m));
            Assert.That(_service.GetMonthlyDeduction(_employeeId, 2026, 3), Is.EqualTo(20000m));
        }

        [Test]
        public void GetMonthlyDeduction_SumsSeveralActiveLoans()
        {
            _service.Save(NewLoan(100000m, 20000m, 2026, 1));
            _service.Save(NewLoan(30000m, 10000m, 2026, 1));

            Assert.That(_service.GetMonthlyDeduction(_employeeId, 2026, 1), Is.EqualTo(30000m));
        }

        [Test]
        public void GetMonthlyDeduction_LastInstalmentIsCappedAtTheBalance()
        {
            // 50 000 principal, 20 000 / month → 20 000, 20 000, then 10 000.
            long id = _service.Save(NewLoan(50000m, 20000m, 2026, 1)).Value;

            _service.RecordPayrollDeductions(_employeeId, 2026, 1);
            _service.RecordPayrollDeductions(_employeeId, 2026, 2);

            Assert.That(_service.GetMonthlyDeduction(_employeeId, 2026, 3), Is.EqualTo(10000m),
                "the final instalment never overshoots the balance");
            Assert.That(_service.GetSummary(id).Outstanding, Is.EqualTo(10000m));
        }

        [Test]
        public void GetMonthlyDeduction_SuspendedLoan_IsNotDeducted()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;
            _service.SetStatus(id, LoanStatus.Suspended);

            Assert.That(_service.GetMonthlyDeduction(_employeeId, 2026, 1), Is.EqualTo(0m));
        }

        // ---------------------------------------------------------------- payroll recording

        [Test]
        public void RecordPayrollDeductions_ReducesTheBalance()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;

            Result<decimal> recorded = _service.RecordPayrollDeductions(_employeeId, 2026, 1);

            Assert.That(recorded.IsSuccess, Is.True, recorded.Error);
            Assert.That(recorded.Value, Is.EqualTo(20000m));
            Assert.That(_service.GetSummary(id).Outstanding, Is.EqualTo(80000m));
            Assert.That(_service.GetSummary(id).Repaid, Is.EqualTo(20000m));
        }

        [Test]
        public void RecordPayrollDeductions_IsIdempotentForAPeriod()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;

            _service.RecordPayrollDeductions(_employeeId, 2026, 1);
            Result<decimal> again = _service.RecordPayrollDeductions(_employeeId, 2026, 1);

            Assert.That(again.Value, Is.EqualTo(0m), "the same period is never recovered twice");
            Assert.That(_service.GetSummary(id).Repaid, Is.EqualTo(20000m));
        }

        [Test]
        public void RecordPayrollDeductions_SettlesTheLoanWhenTheBalanceReachesZero()
        {
            long id = _service.Save(NewLoan(40000m, 20000m, 2026, 1)).Value;

            _service.RecordPayrollDeductions(_employeeId, 2026, 1);
            _service.RecordPayrollDeductions(_employeeId, 2026, 2);

            LoanSummary summary = _service.GetSummary(id);
            Assert.That(summary.Outstanding, Is.EqualTo(0m));
            Assert.That(_service.Get(id).Status, Is.EqualTo(LoanStatus.Settled));

            // A settled loan is no longer deducted.
            Assert.That(_service.GetMonthlyDeduction(_employeeId, 2026, 3), Is.EqualTo(0m));
        }

        [Test]
        public void GetMonthlyDeduction_IsReproducibleAfterRecording()
        {
            // 50 000 / 20 000: month 3 deducts the 10 000 remainder. Re-previewing month 3
            // after it was recorded must still return 10 000, not a fresh instalment.
            _service.Save(NewLoan(50000m, 20000m, 2026, 1));

            _service.RecordPayrollDeductions(_employeeId, 2026, 1);
            _service.RecordPayrollDeductions(_employeeId, 2026, 2);
            decimal previewBefore = _service.GetMonthlyDeduction(_employeeId, 2026, 3);
            _service.RecordPayrollDeductions(_employeeId, 2026, 3);
            decimal previewAfter = _service.GetMonthlyDeduction(_employeeId, 2026, 3);

            Assert.That(previewBefore, Is.EqualTo(10000m));
            Assert.That(previewAfter, Is.EqualTo(10000m), "a recorded period keeps its exact amount");
        }

        [Test]
        public void RecordPayrollDeductions_DoesNotStartBeforeTheLoanBegins()
        {
            _service.Save(NewLoan(100000m, 20000m, 2026, 6));

            Result<decimal> recorded = _service.RecordPayrollDeductions(_employeeId, 2026, 5);

            Assert.That(recorded.Value, Is.EqualTo(0m));
        }

        // ---------------------------------------------------------------- manual repayments

        [Test]
        public void AddManualRepayment_ReducesTheBalance()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;

            Result result = _service.AddManualRepayment(id, 2026, 1, 50000m);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(_service.GetSummary(id).Outstanding, Is.EqualTo(50000m));
        }

        [Test]
        public void AddManualRepayment_AbovetheBalance_IsRejected()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;

            Assert.That(_service.AddManualRepayment(id, 2026, 1, 120000m).IsFailure, Is.True);
        }

        [Test]
        public void AddManualRepayment_SamePeriodTwice_IsRejected()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;
            _service.AddManualRepayment(id, 2026, 1, 10000m);

            Assert.That(_service.AddManualRepayment(id, 2026, 1, 10000m).IsFailure, Is.True,
                "one recovery per loan and period");
        }

        [Test]
        public void RemoveRepayment_ReopensASettledLoan()
        {
            long id = _service.Save(NewLoan(20000m, 20000m, 2026, 1)).Value;
            _service.RecordPayrollDeductions(_employeeId, 2026, 1);
            Assert.That(_service.Get(id).Status, Is.EqualTo(LoanStatus.Settled));

            LoanRepayment repayment = _service.GetRepayments(id).Single();
            Result removed = _service.RemoveRepayment(repayment.Id);

            Assert.That(removed.IsSuccess, Is.True, removed.Error);
            Assert.That(_service.GetSummary(id).Outstanding, Is.EqualTo(20000m));
            Assert.That(_service.Get(id).Status, Is.EqualTo(LoanStatus.Active),
                "removing the final repayment reopens the loan");
        }

        [Test]
        public void ManualThenPayroll_SamePeriod_DoesNotDoubleRecover()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;
            _service.AddManualRepayment(id, 2026, 1, 20000m);

            Result<decimal> payroll = _service.RecordPayrollDeductions(_employeeId, 2026, 1);

            Assert.That(payroll.Value, Is.EqualTo(0m), "the manual repayment already covers the period");
            Assert.That(_service.GetSummary(id).Repaid, Is.EqualTo(20000m));
        }

        // ---------------------------------------------------------------- company + outstanding

        [Test]
        public void GetByCompany_ReturnsEveryLoanWithTheEmployeeName()
        {
            _service.Save(NewLoan(100000m, 20000m, 2026, 1));

            IReadOnlyList<LoanSummary> loans = _service.GetByCompany(_companyId);

            Assert.That(loans.Count, Is.EqualTo(1));
            Assert.That(loans[0].EmployeeName, Is.EqualTo("BENALI Karim"),
                "the name comes from the shared employee table");
        }

        [Test]
        public void GetOutstanding_SumsTheActiveLoansOfTheEmployee()
        {
            _service.Save(NewLoan(100000m, 20000m, 2026, 1));
            long cancelled = _service.Save(NewLoan(50000m, 10000m, 2026, 1)).Value;
            _service.SetStatus(cancelled, LoanStatus.Cancelled);

            Assert.That(_service.GetOutstanding(_employeeId), Is.EqualTo(100000m),
                "a cancelled loan is never counted");
        }

        [Test]
        public void Delete_RemovesTheLoanAndItsRepayments()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;
            _service.RecordPayrollDeductions(_employeeId, 2026, 1);

            Result deleted = _service.Delete(id);

            Assert.That(deleted.IsSuccess, Is.True, deleted.Error);
            Assert.That(_service.Get(id), Is.Null);
            Assert.That(_service.GetMonthlyDeduction(_employeeId, 2026, 2), Is.EqualTo(0m));
        }

        [Test]
        public void Save_PrincipalBelowAlreadyRepaid_IsRejected()
        {
            long id = _service.Save(NewLoan(100000m, 20000m, 2026, 1)).Value;
            _service.RecordPayrollDeductions(_employeeId, 2026, 1); // 20 000 repaid

            Loan loan = _service.Get(id);
            loan.Principal = 10000m;

            Assert.That(_service.Save(loan).IsFailure, Is.True,
                "the principal cannot fall below what was already recovered");
        }

        private Loan NewLoan(decimal principal, decimal installment, int year, int month)
        {
            return new Loan
            {
                EmployeeId = _employeeId,
                Type = LoanType.Loan,
                Principal = principal,
                MonthlyInstallment = installment,
                StartYear = year,
                StartMonth = month,
                Reason = "Test"
            };
        }
    }
}
