using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Payslip correction / deletion / restore (<see cref="PayslipRevisionService"/>).
    /// <para>
    /// The loan-repayment reversal is the ONE stateful side effect that touches real money, so the
    /// loan-atomicity tests come first: they prove the payslip status change and the loan reversal /
    /// re-application are one all-or-nothing transaction. The remaining tests prove the downstream
    /// consistency the feature promises: corrected figures flow into the CNAS DAC with the old value
    /// gone, a deleted payslip is excluded everywhere and frees its slot, the "one active per period"
    /// rule still holds, and an upgraded database is unchanged.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class PayslipRevisionServiceTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private ContractService _contracts;
        private ArchiveService _archive;
        private ConfigurationService _config;
        private PayrollService _payroll;
        private LoanService _loans;
        private BatchPayrollService _batch;
        private CnasDeclarationService _cnas;
        private PayslipRevisionService _revision;

        private static readonly int Year = DateTime.Today.Year - 1;
        private const int Month = 3;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-revision-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _contracts = new ContractService(_uow);
            _archive = new ArchiveService(_uow);
            _config = new ConfigurationService(_uow);
            _payroll = new PayrollService(_uow, _config, new PayrollCalculationEngine());
            _loans = new LoanService(_uow);
            _batch = new BatchPayrollService(
                _employees, new PayrollElementService(_uow, new PayrollElementValidator()),
                _loans, new AttendanceService(_uow), _payroll, _contracts, _archive, _ => true);
            _cnas = new CnasDeclarationService(_companies, _employees, _archive, _config);
            _revision = new PayslipRevisionService(_uow, _payroll, _config, _loans);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // =================================================================== loans (the real risk)

        [Test]
        public void Cancel_ReversesTheLoanRepayment_InOneTransaction()
        {
            Seed(out long companyId, out long empId);
            long loanId = AddLoan(empId, principal: 100000m, instalment: 20000m);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            Assert.That(_loans.GetSummary(loanId).Outstanding, Is.EqualTo(80000m), "baseline: one instalment recovered");

            Result cancel = _revision.Cancel(payslipId, "Erreur de saisie");

            Assert.That(cancel.IsSuccess, Is.True, cancel.Error);
            Assert.That(Payslip(payslipId).Status, Is.EqualTo(PayslipStatus.Cancelled));
            Assert.That(Repayment(loanId, Year, Month), Is.Null, "the payroll repayment is reversed");
            Assert.That(_loans.GetSummary(loanId).Outstanding, Is.EqualTo(100000m), "balance restored");
        }

        [Test]
        public void Cancel_WhenLoanReversalThrows_RollsBackThePayslipStatus()
        {
            // The loan reversal is the risky step. If it throws, the whole operation must roll back:
            // there is no state where the payslip is cancelled but the repayment survives.
            Seed(out long companyId, out long empId);
            long loanId = AddLoan(empId, 100000m, 20000m);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            var sabotaged = new PayslipRevisionService(_uow, _payroll, _config, new ThrowingLoanService(_loans, onReverse: true));
            Result cancel = sabotaged.Cancel(payslipId, "motif");

            Assert.That(cancel.IsSuccess, Is.False, "the operation must fail, not half-apply");
            Assert.That(Payslip(payslipId).Status, Is.EqualTo(PayslipStatus.Active), "payslip NOT cancelled (rolled back)");
            Assert.That(Repayment(loanId, Year, Month), Is.Not.Null, "repayment still present");
            Assert.That(_loans.GetSummary(loanId).Outstanding, Is.EqualTo(80000m), "balance unchanged");
        }

        [Test]
        public void Correct_WhenLoanReapplyThrows_RollsBackTheNewVersionAndTheSupersede()
        {
            Seed(out long companyId, out long empId);
            long loanId = AddLoan(empId, 100000m, 20000m);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            PayrollGenerationRequest amended = _batch.BuildRequest(companyId, empId, Year, Month);
            var sabotaged = new PayslipRevisionService(_uow, _payroll, _config, new ThrowingLoanService(_loans, onApply: true));
            Result<long> correct = sabotaged.Correct(new PayslipCorrectionRequest
            {
                OriginalPayslipId = payslipId, Amended = amended, Reason = "test"
            });

            Assert.That(correct.IsSuccess, Is.False);
            Assert.That(Payslip(payslipId).Status, Is.EqualTo(PayslipStatus.Active), "original NOT superseded (rolled back)");
            Assert.That(ActivePayslips(companyId, Month).Count, Is.EqualTo(1), "no orphan corrected version persisted");
            Assert.That(_loans.GetSummary(loanId).Outstanding, Is.EqualTo(80000m), "loan balance unchanged");
        }

        [Test]
        public void Correct_ReversesTheOldRecoveryAndReappliesTheCorrectedOne()
        {
            Seed(out long companyId, out long empId);
            long loanId = AddLoan(empId, 100000m, 20000m);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            // Corrected worksheet reduces the loan recovery line from 20 000 to 10 000.
            PayrollGenerationRequest amended = _batch.BuildRequest(companyId, empId, Year, Month);
            SetLoanLine(amended, 10000m);

            Result<long> correct = _revision.Correct(new PayslipCorrectionRequest
            {
                OriginalPayslipId = payslipId, Amended = amended, Reason = "Mensualité ajustée"
            });

            Assert.That(correct.IsSuccess, Is.True, correct.Error);
            Assert.That(Repayment(loanId, Year, Month).Amount, Is.EqualTo(10000m), "schedule credited exactly the corrected line");
            Assert.That(_loans.GetSummary(loanId).Outstanding, Is.EqualTo(90000m));
        }

        [Test]
        public void Restore_ReappliesTheLoanRecovery()
        {
            Seed(out long companyId, out long empId);
            long loanId = AddLoan(empId, 100000m, 20000m);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            _revision.Cancel(payslipId, "annulation temporaire");
            Assert.That(_loans.GetSummary(loanId).Outstanding, Is.EqualTo(100000m));

            Result restore = _revision.Restore(payslipId);

            Assert.That(restore.IsSuccess, Is.True, restore.Error);
            Assert.That(Payslip(payslipId).Status, Is.EqualTo(PayslipStatus.Active));
            Assert.That(_loans.GetSummary(loanId).Outstanding, Is.EqualTo(80000m), "recovery re-applied on restore");
        }

        // =================================================================== downstream consistency

        [Test]
        public void Correct_UpdatesTheDacAssiette_AndTheOldValueIsGone()
        {
            Seed(out long companyId, out long empId);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);
            decimal oldAssiette = _cnas.BuildDac(companyId, Year, new[] { Month }).Assiette;

            // Raise the base salary by 10 000 for the correction.
            PayrollGenerationRequest amended = _batch.BuildRequest(companyId, empId, Year, Month);
            amended.BaseSalaryOverride = 60000m; // was 50 000

            Result<long> correct = _revision.Correct(new PayslipCorrectionRequest
            {
                OriginalPayslipId = payslipId, Amended = amended, Reason = "Rappel de salaire"
            });
            Assert.That(correct.IsSuccess, Is.True, correct.Error);

            decimal newAssiette = _cnas.BuildDac(companyId, Year, new[] { Month }).Assiette;
            decimal correctedBase = Payslip(correct.Value).BaseCotisable;

            Assert.That(newAssiette, Is.EqualTo(correctedBase), "DAC assiette = the corrected payslip's base");
            Assert.That(newAssiette, Is.GreaterThan(oldAssiette), "the raise is reflected");
            Assert.That(ActivePayslips(companyId, Month).Count, Is.EqualTo(1), "only the corrected version is active");
            Assert.That(ActivePayslips(companyId, Month).Single().Id, Is.EqualTo(correct.Value));
        }

        [Test]
        public void Cancel_ExcludesThePayslipFromTheDacAndTheArchive()
        {
            Seed(out long companyId, out long empId);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);
            Assert.That(_cnas.BuildDac(companyId, Year, new[] { Month }).Assiette, Is.GreaterThan(0m));

            _revision.Cancel(payslipId, "doublon");

            Assert.That(_cnas.BuildDac(companyId, Year, new[] { Month }).Assiette, Is.EqualTo(0m),
                "a cancelled payslip is no longer declared");
            Assert.That(ActivePayslips(companyId, Month), Is.Empty, "and no longer listed in the archive");
        }

        [Test]
        public void Cancel_FreesTheSlot_AndTheMonthCanBeRecalculated()
        {
            Seed(out long companyId, out long empId);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            _revision.Cancel(payslipId, "recalcul");

            Result<long> rerun = _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, Month));
            Assert.That(rerun.IsSuccess, Is.True, rerun.Error);
            Assert.That(rerun.Value, Is.Not.EqualTo(payslipId), "a brand-new active payslip");
            Assert.That(ActivePayslips(companyId, Month).Count, Is.EqualTo(1));
        }

        // =================================================================== uniqueness & guards

        [Test]
        public void Uniqueness_TwoActivePayslipsForTheSamePeriodAreImpossible()
        {
            Seed(out long companyId, out long empId);
            _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, Month));

            Result<long> second = _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, Month));

            Assert.That(second.IsSuccess, Is.False, "the duplicate active payslip is refused");
            Assert.That(ActivePayslips(companyId, Month).Count, Is.EqualTo(1));
        }

        [Test]
        public void Restore_IsRefused_WhenANewerActivePayslipExists()
        {
            Seed(out long companyId, out long empId);
            long original = GenerateWithLoanRecovery(companyId, empId, Month);
            _revision.Cancel(original, "remplacement");
            long replacement = _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, Month)).Value;

            Result restore = _revision.Restore(original);

            Assert.That(restore.IsSuccess, Is.False, "cannot restore over an active payslip");
            Assert.That(Payslip(original).Status, Is.EqualTo(PayslipStatus.Cancelled));
            Assert.That(Payslip(replacement).Status, Is.EqualTo(PayslipStatus.Active));
        }

        [Test]
        public void Correct_RequiresAReason()
        {
            Seed(out long companyId, out long empId);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            Result<long> correct = _revision.Correct(new PayslipCorrectionRequest
            {
                OriginalPayslipId = payslipId,
                Amended = _batch.BuildRequest(companyId, empId, Year, Month),
                Reason = "   "
            });

            Assert.That(correct.IsSuccess, Is.False);
            Assert.That(correct.Error, Does.Contain("motif").IgnoreCase);
        }

        [Test]
        public void Cancel_RequiresAReason()
        {
            Seed(out long companyId, out long empId);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            Result cancel = _revision.Cancel(payslipId, "");

            Assert.That(cancel.IsSuccess, Is.False);
            Assert.That(ActivePayslips(companyId, Month).Count, Is.EqualTo(1), "nothing changed");
        }

        [Test]
        public void History_ListsSupersededAndCancelledPayslips_WithTheReason()
        {
            Seed(out long companyId, out long empId);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);
            _revision.Correct(new PayslipCorrectionRequest
            {
                OriginalPayslipId = payslipId,
                Amended = _batch.BuildRequest(companyId, empId, Year, Month),
                Reason = "Correction A"
            });

            IReadOnlyList<PayslipHistoryRow> history = _revision.GetHistory(companyId, Year, Month);

            Assert.That(history.Count, Is.EqualTo(1), "the superseded original appears in history");
            Assert.That(history[0].Status, Is.EqualTo(PayslipStatus.Superseded));
            Assert.That(history[0].Reason, Is.EqualTo("Correction A"));
        }

        // =================================================================== migration additivity

        [Test]
        public void FreshlyGeneratedPayslip_IsActiveByDefault_AndVisibleEverywhere()
        {
            // The 0036 column default is Active, so a payslip produced by the unchanged generation
            // path behaves exactly as before the migration — the proxy for "an upgraded database's
            // existing rows are untouched".
            Seed(out long companyId, out long empId);
            long payslipId = GenerateWithLoanRecovery(companyId, empId, Month);

            Assert.That(Payslip(payslipId).Status, Is.EqualTo(PayslipStatus.Active));
            Assert.That(Payslip(payslipId).SupersedesPayslipId, Is.Null);
            Assert.That(ActivePayslips(companyId, Month).Count, Is.EqualTo(1));
            Assert.That(_cnas.BuildDac(companyId, Year, new[] { Month }).Assiette, Is.GreaterThan(0m));
        }

        // =================================================================== helpers

        private void Seed(out long companyId, out long empId)
        {
            companyId = _companies.Create(new Company
            {
                NameFr = "SARL Revision", Nif = "000000000000000", CnasEmployerNumber = "1234567890"
            }).Value;
            empId = _employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "BENALI", FirstNameFr = "Karim", Poste = "Agent",
                Nss = "123456789012", BirthDate = new DateTime(1985, 1, 1), Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash, ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year - 2, 1, 1), BaseSalary = 50000m, IsActive = true
            }).Value;
        }

        private long AddLoan(long empId, decimal principal, decimal instalment)
        {
            return _loans.Save(new Loan
            {
                EmployeeId = empId, Type = LoanType.Loan, Principal = principal,
                MonthlyInstallment = instalment, StartYear = Year, StartMonth = Month
            }).Value;
        }

        /// <summary>Generates a payslip and records its loan recovery exactly as the worksheet/batch do.</summary>
        private long GenerateWithLoanRecovery(long companyId, long empId, int month)
        {
            Result<long> generated = _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, month));
            Assert.That(generated.IsSuccess, Is.True, generated.Error);

            Payslip archived = _archive.GetPayslip(generated.Value);
            decimal withheld = archived.Details
                .Where(d => d.ElementType == ElementType.Deduction &&
                            string.Equals(d.LabelFr, "Remboursement prêt", StringComparison.Ordinal))
                .Sum(d => d.Amount);
            if (withheld > 0m) _loans.RecordPayrollDeductions(empId, Year, month, withheld);

            return generated.Value;
        }

        private static void SetLoanLine(PayrollGenerationRequest request, decimal amount)
        {
            PayrollElementEntry loan = request.Elements.FirstOrDefault(e =>
                e.IsManual && string.Equals(e.ManualLabel, "Remboursement prêt", StringComparison.Ordinal));
            Assert.That(loan, Is.Not.Null, "the request should carry the automatic loan line");
            loan.LineAmount = amount;
        }

        private Payslip Payslip(long id)
        {
            using (IUnitOfWork uow = _uow.Create()) return uow.Payslips.GetById(id);
        }

        private LoanRepayment Repayment(long loanId, int year, int month)
        {
            using (IUnitOfWork uow = _uow.Create()) return uow.Loans.GetRepayment(loanId, year, month);
        }

        private List<Payslip> ActivePayslips(long companyId, int month)
        {
            PayrollRun run = _archive.SearchRuns(companyId, Year, month).FirstOrDefault();
            if (run == null) return new List<Payslip>();
            return _archive.GetRun(run.Id).Payslips.ToList();
        }

        /// <summary>A loan service that delegates everything to the real one but throws on a chosen
        /// step, to prove the revision operation rolls back fully instead of half-applying.</summary>
        private sealed class ThrowingLoanService : ILoanService
        {
            private readonly ILoanService _inner;
            private readonly bool _onReverse;
            private readonly bool _onApply;

            public ThrowingLoanService(ILoanService inner, bool onReverse = false, bool onApply = false)
            {
                _inner = inner; _onReverse = onReverse; _onApply = onApply;
            }

            public void ReversePayrollRepayment(IUnitOfWork uow, long employeeId, int year, int month)
            {
                if (_onReverse) throw new InvalidOperationException("sabotage: reversal");
                _inner.ReversePayrollRepayment(uow, employeeId, year, month);
            }

            public decimal ApplyPayrollRepayment(IUnitOfWork uow, long employeeId, int year, int month, decimal withheld)
            {
                if (_onApply) throw new InvalidOperationException("sabotage: apply");
                return _inner.ApplyPayrollRepayment(uow, employeeId, year, month, withheld);
            }

            // -- straight delegation --
            public Result<long> Save(Loan loan) => _inner.Save(loan);
            public Result SetStatus(long id, LoanStatus status) => _inner.SetStatus(id, status);
            public Result Delete(long id) => _inner.Delete(id);
            public Loan Get(long id) => _inner.Get(id);
            public LoanSummary GetSummary(long loanId) => _inner.GetSummary(loanId);
            public IReadOnlyList<LoanSummary> GetByEmployee(long employeeId) => _inner.GetByEmployee(employeeId);
            public IReadOnlyList<LoanSummary> GetByCompany(long companyId) => _inner.GetByCompany(companyId);
            public LoanPortfolio GetActivePortfolio(long companyId) => _inner.GetActivePortfolio(companyId);
            public IReadOnlyList<LoanRepayment> GetRepayments(long loanId) => _inner.GetRepayments(loanId);
            public Result AddManualRepayment(long loanId, int year, int month, decimal amount) => _inner.AddManualRepayment(loanId, year, month, amount);
            public Result RemoveRepayment(long repaymentId) => _inner.RemoveRepayment(repaymentId);
            public decimal GetMonthlyDeduction(long employeeId, int year, int month) => _inner.GetMonthlyDeduction(employeeId, year, month);
            public Result<decimal> RecordPayrollDeductions(long employeeId, int year, int month) => _inner.RecordPayrollDeductions(employeeId, year, month);
            public Result<decimal> RecordPayrollDeductions(long employeeId, int year, int month, decimal actualWithheld) => _inner.RecordPayrollDeductions(employeeId, year, month, actualWithheld);
            public decimal GetOutstanding(long employeeId) => _inner.GetOutstanding(employeeId);
        }
    }
}
