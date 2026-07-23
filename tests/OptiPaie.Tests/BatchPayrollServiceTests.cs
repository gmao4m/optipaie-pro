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
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The company-wide batch payroll run. Proves it is pure orchestration over the existing
    /// per-employee path: identical figures, one shared run record, and blocking employees
    /// skipped-and-reported rather than silently paid wrong. On real SQLite, real services.
    /// </summary>
    [TestFixture]
    public sealed class BatchPayrollServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uow;

        private ICompanyService _companies;
        private IEmployeeService _employees;
        private IContractService _contracts;
        private IPayrollElementService _elements;
        private ILoanService _loans;
        private IAttendanceService _attendance;
        private IArchiveService _archive;
        private IPayrollService _payroll;
        private IBatchPayrollService _batch;

        private long _companyId;
        private static readonly int Year = DateTime.Today.Year;
        private const int Month = 6;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-batch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _contracts = new ContractService(_uow);
            _elements = new PayrollElementService(_uow, new PayrollElementValidator());
            _loans = new LoanService(_uow);
            _attendance = new AttendanceService(_uow);
            _archive = new ArchiveService(_uow);
            _payroll = new PayrollService(_uow, new ConfigurationService(_uow), new PayrollCalculationEngine());
            _batch = new BatchPayrollService(_employees, _elements, _loans, _attendance, _payroll, _contracts, _archive, _ => true);

            _companyId = _companies.Create(new Company { NameFr = "SARL Batch", Nif = "000000000000000" }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        private long AddEmployee(string last, decimal salary, bool withActiveContract = true)
        {
            long id = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = last, FirstNameFr = "Karim", Poste = "Agent",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(Year - 2, 1, 1), BaseSalary = salary, IsActive = true
            }).Value;

            if (withActiveContract)
            {
                long contractId = _contracts.CreateDraftFromEmployee(id).Value;
                _contracts.Activate(contractId);
            }
            return id;
        }

        // An independent reconstruction of the single-employee request, from raw data — used
        // to confirm the batch service computes the very same inputs the worksheet does.
        private PayrollGenerationRequest SinglePathRequest(long employeeId)
        {
            Employee e = _employees.Get(employeeId);
            decimal monthDays = DateTime.DaysInMonth(Year, Month);
            var elements = new List<PayrollElementEntry>();
            foreach (EmployeeElement a in _employees.GetElements(employeeId).Where(x => x.IsActive))
            {
                PayrollElement el = _elements.Get(a.ElementId);
                if (el == null || el.IsDeleted || !el.IsEnabled) continue;
                elements.Add(new PayrollElementEntry
                {
                    ElementId = el.Id,
                    LineAmount = a.Amount ?? el.DefaultAmount ?? 0m,
                    IsManual = false,
                    ManualLabel = el.NameFr,
                    ManualType = el.ElementType == ElementType.Gain ? ElementType.Gain : ElementType.Deduction
                });
            }
            decimal loanDue = _loans.GetMonthlyDeduction(employeeId, Year, Month);
            if (loanDue > 0m)
            {
                elements.Add(new PayrollElementEntry
                {
                    ElementId = 0, LineAmount = loanDue, IsManual = true,
                    ManualLabel = "Remboursement prêt", ManualType = ElementType.Deduction
                });
            }
            decimal worked = monthDays, hours = 0m;
            AttendanceSummary s = _attendance.GetMonthlySummary(employeeId, Year, Month);
            if (s != null && s.RecordedDays > 0) { worked = Math.Max(0m, monthDays - s.AbsentDays); hours = s.WorkedHours; }

            return new PayrollGenerationRequest
            {
                CompanyId = _companyId, EmployeeId = employeeId, Year = Year, Month = Month,
                WorkedDays = worked, WorkableDays = monthDays, WorkedHours = hours,
                BaseSalaryOverride = e.BaseSalary, Elements = elements
            };
        }

        private static void AssertTotalsEqual(PayrollTotals a, PayrollTotals b, string who)
        {
            Assert.That(a.SalaireBrut, Is.EqualTo(b.SalaireBrut), who + " brut");
            Assert.That(a.BaseCotisable, Is.EqualTo(b.BaseCotisable), who + " base cotisable");
            Assert.That(a.CnasEmployee, Is.EqualTo(b.CnasEmployee), who + " CNAS");
            Assert.That(a.BaseImposable, Is.EqualTo(b.BaseImposable), who + " base imposable");
            Assert.That(a.IrgBrut, Is.EqualTo(b.IrgBrut), who + " IRG brut");
            Assert.That(a.Abattement, Is.EqualTo(b.Abattement), who + " abattement");
            Assert.That(a.Irg, Is.EqualTo(b.Irg), who + " IRG");
            Assert.That(a.NetSalaire, Is.EqualTo(b.NetSalaire), who + " net");
        }

        [Test]
        public void Batch_ProducesIdenticalFigures_ToTheSingleEmployeePath()
        {
            // Three varied employees: plain, with an assigned prime + a loan, and with absences.
            long plain = AddEmployee("BENALI", 60000m);

            long withPrime = AddEmployee("TOUATI", 55000m);
            PayrollElement gain = _elements.GetAll(false).First(e => e.ElementType == ElementType.Gain && e.IsEnabled);
            _employees.AssignElement(new EmployeeElement { EmployeeId = withPrime, ElementId = gain.Id, Amount = 12000m, IsActive = true });
            _loans.Save(new Loan { EmployeeId = withPrime, Type = LoanType.Loan, Principal = 100000m, MonthlyInstallment = 15000m, StartYear = Year, StartMonth = 1 });

            long withAbsence = AddEmployee("AMRANI", 48000m);
            _attendance.SetDayStatus(withAbsence, new DateTime(Year, Month, 3), AttendanceStatus.Present);
            _attendance.SetDayStatus(withAbsence, new DateTime(Year, Month, 4), AttendanceStatus.Absent);
            _attendance.SetDayStatus(withAbsence, new DateTime(Year, Month, 5), AttendanceStatus.Absent);

            // Batch request vs an independent single-path request — identical calculated figures.
            foreach (long id in new[] { plain, withPrime, withAbsence })
            {
                PayrollResult batch = _payroll.Preview(_batch.BuildRequest(_companyId, id, Year, Month));
                PayrollResult single = _payroll.Preview(SinglePathRequest(id));
                Assert.That(batch.IsSuccess, Is.True);
                Assert.That(single.IsSuccess, Is.True);
                AssertTotalsEqual(batch.Totals, single.Totals, "emp " + id);
            }
        }

        [Test]
        public void Run_ArchivesEveryEmployeeIntoOneRun_AndNetMatchesTheEngine()
        {
            long a = AddEmployee("BENALI", 60000m);
            long b = AddEmployee("TOUATI", 55000m);
            long c = AddEmployee("AMRANI", 48000m);

            var ticks = new List<BatchProgress>();
            BatchPayrollResult result = _batch.Run(_companyId, Year, Month, new SyncProgress(ticks));

            Assert.That(result.Succeeded, Is.EqualTo(3));
            Assert.That(result.Failed, Is.EqualTo(0));
            Assert.That(result.Skipped, Is.EqualTo(0));

            // ONE shared payroll run holds all three payslips.
            Assert.That(result.RunId, Is.GreaterThan(0));
            Assert.That(_archive.SearchRuns(_companyId, Year, Month).Count, Is.EqualTo(1), "exactly one run for the period");
            Assert.That(_archive.SearchRuns(_companyId, Year, Month).Single().Id, Is.EqualTo(result.RunId));

            // Each reported net equals a fresh engine calculation of that employee.
            foreach (BatchEmployeeResult r in result.Results)
            {
                decimal engineNet = _payroll.Preview(_batch.BuildRequest(_companyId, r.EmployeeId, Year, Month)).Totals.NetSalaire;
                Assert.That(r.Net, Is.EqualTo(engineNet), "net of " + r.EmployeeName);
                Assert.That(r.PayslipId, Is.GreaterThan(0));
            }

            // Progress was reported and reached completion.
            Assert.That(ticks.Count, Is.GreaterThan(0));
            Assert.That(ticks.Last().Done, Is.EqualTo(3));
            Assert.That(ticks.Last().Total, Is.EqualTo(3));
        }

        [Test]
        public void Plan_And_Run_CatchABlockingEmployee_NeverSilentlyPayThemWrong()
        {
            AddEmployee("BENALI", 60000m);                       // ready
            long noContract = AddEmployee("KACI", 50000m, withActiveContract: false); // BLOCKING
            long noSalary = AddEmployee("ZID", 0m, withActiveContract: true);          // BLOCKING (no salary)

            // Pre-run plan surfaces the blocks before anything runs.
            BatchPayrollPlan plan = _batch.Plan(_companyId, Year, Month);
            Assert.That(plan.TotalActive, Is.EqualTo(3));
            Assert.That(plan.Ready, Is.EqualTo(1));
            Assert.That(plan.Blocked, Is.EqualTo(2));
            BatchEmployeeCheck nc = plan.Employees.Single(e => e.EmployeeId == noContract);
            Assert.That(nc.Severity, Is.EqualTo(BatchCheckSeverity.Blocking));
            Assert.That(nc.Reason, Does.Contain("contrat"));

            // The run skips them (no payslip) and reports exactly why.
            BatchPayrollResult result = _batch.Run(_companyId, Year, Month);
            Assert.That(result.Succeeded, Is.EqualTo(1));
            Assert.That(result.Skipped, Is.EqualTo(2));
            Assert.That(result.IsComplete, Is.False, "a run with skips must not look complete");
            BatchEmployeeResult skipped = result.Results.Single(r => r.EmployeeId == noContract);
            Assert.That(skipped.Outcome, Is.EqualTo(BatchOutcome.Skipped));
            Assert.That(skipped.PayslipId, Is.EqualTo(0), "no payslip was produced for the blocked employee");
            Assert.That(_archive.GetPayslipsByEmployee(noContract).Count, Is.EqualTo(0));
        }

        [Test]
        public void Run_ArchivesAPayslipCarryingALoanLine_AndItReRenders()
        {
            // A loan instalment adds a free (manual) deduction line with no catalog element.
            // Such a payslip must archive (its detail stores a NULL element id, not 0) and
            // re-render from the archive with all its lines — regression guard.
            long id = AddEmployee("BENALI", 60000m);
            _loans.Save(new Loan { EmployeeId = id, Type = LoanType.Loan, Principal = 90000m, MonthlyInstallment = 15000m, StartYear = Year, StartMonth = 1 });
            Assert.That(_loans.GetMonthlyDeduction(id, Year, Month), Is.GreaterThan(0m), "an instalment is due this period");

            BatchPayrollResult result = _batch.Run(_companyId, Year, Month);
            Assert.That(result.Succeeded, Is.EqualTo(1), "the loan payslip archives, it does not fail to persist");

            // Re-render path: the archived payslip loads with its details, including the loan line.
            Payslip payslip = _archive.GetPayslip(result.Results[0].PayslipId);
            Assert.That(payslip, Is.Not.Null);
            Assert.That(payslip.Details.Any(d => d.LabelFr != null && d.LabelFr.Contains("prêt")), Is.True, "the loan line is archived");
            Assert.That(payslip.Details.Where(d => d.LabelFr != null && d.LabelFr.Contains("prêt")).All(d => d.ElementId == null), Is.True,
                "the manual loan line stores a null element id");
        }

        [Test]
        public void Run_IsIdempotent_ReRunningReportsAlreadyPaid_NotADuplicate()
        {
            AddEmployee("BENALI", 60000m);
            _batch.Run(_companyId, Year, Month);

            BatchPayrollResult again = _batch.Run(_companyId, Year, Month);
            Assert.That(again.Succeeded, Is.EqualTo(0));
            Assert.That(again.Failed, Is.EqualTo(1), "the already-paid employee is reported, not double-paid");
            Assert.That(_archive.SearchRuns(_companyId, Year, Month).Single().Id, Is.EqualTo(again.RunId));
        }

        /// <summary>Captures progress ticks synchronously for assertions.</summary>
        private sealed class SyncProgress : IProgress<BatchProgress>
        {
            private readonly List<BatchProgress> _sink;
            public SyncProgress(List<BatchProgress> sink) { _sink = sink; }
            public void Report(BatchProgress value) { _sink.Add(value); }
        }
    }
}
