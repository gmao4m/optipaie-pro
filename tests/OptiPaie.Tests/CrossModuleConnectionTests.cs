using System;
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
    /// PHASE 4 — the cross-module connection audit. Proves the product's headline promise:
    /// an employee entered once flows into every module (contract, attendance, payroll,
    /// assets, training, loans, leave, dashboard, reports) through the ONE shared Employees
    /// record — never re-typed — on real SQLite through the real services.
    /// </summary>
    [TestFixture]
    public sealed class CrossModuleConnectionTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uow;

        private ICompanyService _companies;
        private IEmployeeService _employees;
        private IContractService _contracts;
        private IAttendanceService _attendance;
        private ILeaveService _leave;
        private ILoanService _loans;
        private IAtsService _ats;
        private IAssetService _assets;
        private ITrainingService _training;
        private IPayrollService _payroll;
        private IReportService _reports;
        private IDashboardService _dashboard;

        private long _companyId;
        private long _employeeId;
        private static readonly int Year = DateTime.Today.Year;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-conn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _contracts = new ContractService(_uow);
            _attendance = new AttendanceService(_uow);
            _leave = new LeaveService(_uow);
            _loans = new LoanService(_uow);
            _ats = new AtsService(_uow);
            _assets = new AssetService(_uow);
            _training = new TrainingService(_uow);
            _payroll = new PayrollService(_uow, new ConfigurationService(_uow), new PayrollCalculationEngine());
            _reports = new ReportService(_companies, _employees, _attendance, _leave, _loans, _ats, _assets, _training);
            _dashboard = new DashboardService(_companies, _employees, _contracts, _leave, _loans, _attendance, _ats, _assets, _training);

            _companyId = _companies.Create(new Company { NameFr = "SARL Connexion", Nif = "000000000000000" }).Value;
            _employeeId = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "BENALI", FirstNameFr = "Karim", Department = "Production",
                Poste = "Opérateur", Gender = Gender.Male, MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash, ContractType = ContractType.Cdi,
                HireDate = new DateTime(Year, 1, 5), BaseSalary = 40000m, IsActive = true
            }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        [Test]
        public void Employee_AddedOnce_FlowsThroughEveryModule()
        {
            // ---- Arrow: Employee → auto-draft Contract (created from the employee's terms) ----
            long contractId = _contracts.CreateDraftFromEmployee(_employeeId).Value;
            ContractSummary draft = _contracts.GetSummary(contractId);
            Assert.That(draft.Status, Is.EqualTo(ContractStatus.Draft));
            Assert.That(draft.EmployeeId, Is.EqualTo(_employeeId), "the contract references the shared employee");
            Assert.That(draft.Position, Is.EqualTo("Opérateur"), "terms carried from the employee, not re-typed");
            Assert.That(draft.BaseSalary, Is.EqualTo(40000m));

            // ---- Arrow: Employee → Attendance (matrix is keyed on the shared roster) ----
            _attendance.SetDayStatus(_employeeId, new DateTime(Year, 3, 3), AttendanceStatus.Present);
            _attendance.SetDayStatus(_employeeId, new DateTime(Year, 3, 4), AttendanceStatus.Absent);
            AttendanceSummary att = _attendance.GetMonthlySummary(_employeeId, Year, 3);
            Assert.That(att.PresentDays, Is.EqualTo(1));
            Assert.That(_attendance.GetCompanyMonthlySummary(_companyId, Year, 3).Any(s => s.EmployeeId == _employeeId),
                Is.True, "the employee shows up in the company attendance matrix");

            // ---- Arrow: Employee → Assets (assign by holder, resolved from the shared record) ----
            long assetId = _assets.Save(new Asset
            {
                CompanyId = _companyId, Name = "Perceuse Bosch", Category = AssetCategory.Tool, PurchaseValue = 35000m
            }).Value;
            Assert.That(_assets.Assign(assetId, _employeeId, new DateTime(Year, 2, 1), "Neuf", null).IsSuccess);
            Assert.That(_assets.GetHeldByEmployee(_employeeId).Any(a => a.AssetId == assetId),
                Is.True, "the asset is linked to the employee");

            // ---- Arrow: Employee → Training (enrolment resolves the shared employee) ----
            long sessionId = _training.Save(new TrainingSession
            {
                CompanyId = _companyId, Title = "Sécurité chantier", Category = "HSE",
                Status = TrainingStatus.Planned, StartDate = new DateTime(Year, 4, 1), Cost = 50000m
            }).Value;
            Assert.That(_training.Enroll(sessionId, _employeeId).IsSuccess);
            Assert.That(_training.GetParticipants(sessionId).Any(p => p.EmployeeId == _employeeId && p.EmployeeName.Contains("BENALI")),
                Is.True, "the participant name is resolved live from the employee record");

            // ---- Arrow: Employee → Loans ----
            _loans.Save(new Loan
            {
                EmployeeId = _employeeId, Type = LoanType.Loan, Principal = 100000m,
                MonthlyInstallment = 20000m, StartYear = Year, StartMonth = 1
            });
            Assert.That(_loans.GetByCompany(_companyId).Any(l => l.EmployeeName.Contains("BENALI")),
                Is.True, "the loan lists the employee by their shared name");

            // ---- Arrow: Employee → Leave (a pending request awaiting approval) ----
            _leave.Save(new LeaveRequest
            {
                EmployeeId = _employeeId, Type = LeaveType.Annual, Status = LeaveStatus.Pending,
                StartDate = new DateTime(Year, 5, 2), EndDate = new DateTime(Year, 5, 6), Days = 4
            });

            // ---- Arrow: every module → Dashboard aggregation ----
            DashboardSnapshot dash = _dashboard.Build();
            Assert.That(dash.Employees, Is.GreaterThanOrEqualTo(1));
            Assert.That(dash.AssetsAssigned, Is.GreaterThanOrEqualTo(1), "dashboard sees the assigned asset");
            Assert.That(dash.ActiveLoans, Is.GreaterThanOrEqualTo(1), "dashboard sees the loan");
            Assert.That(dash.PendingLeave, Is.GreaterThanOrEqualTo(1), "dashboard surfaces the leave to approve");

            // ---- Arrow: every module → Reports Center (all read the shared employee) ----
            Assert.That(_reports.Build(ReportService.Headcount, _companyId, Year, 1).Rows
                .Any(r => r[1] == "BENALI Karim"), Is.True, "the roster report lists the employee");
            Assert.That(_reports.Build(ReportService.Assets, _companyId, Year, 1).Rows
                .Any(r => r.Contains("BENALI Karim")), Is.True, "the assets report resolves the holder");
            Assert.That(_reports.Build(ReportService.Loans, _companyId, Year, 1).Rows
                .Any(r => r.Contains("BENALI Karim")), Is.True, "the loans report lists the employee");

            // ---- Arrow: Termination → exit clearance (held assets remain listed to hand back) ----
            Assert.That(_contracts.Activate(contractId).IsSuccess);
            Assert.That(_contracts.Terminate(contractId, new DateTime(Year, 6, 30), "Fin de contrat").IsSuccess);
            Employee afterExit = _employees.Get(_employeeId);
            Assert.That(afterExit.IsActive, Is.False, "termination marks the employee as exited");
            Assert.That(afterExit.ExitDate.HasValue, Is.True);
            Assert.That(afterExit.ExitDate.Value.Date, Is.EqualTo(new DateTime(Year, 6, 30)), "exit date recorded");
            Assert.That(_assets.GetHeldByEmployee(_employeeId).Any(a => a.AssetId == assetId),
                Is.True, "the still-held asset is available for the exit-clearance checklist");
        }

        [Test]
        public void Payroll_FollowsTheContractSetSalary_NeverAReTypedNumber()
        {
            // Baseline: the employee is at 40 000; payroll (no override) reads that.
            decimal brutAt40 = RunPayroll();
            Assert.That(brutAt40, Is.GreaterThan(0m));

            // A contract raises the salary to 55 000 and is activated → SyncEmployee pushes
            // the new term onto the shared employee. Nothing is re-entered in payroll.
            long contractId = _contracts.CreateDraftFromEmployee(_employeeId).Value;
            EmploymentContract contract = _contracts.Get(contractId);
            contract.BaseSalary = 55000m;
            _contracts.Save(contract);
            Assert.That(_contracts.Activate(contractId).IsSuccess);

            Assert.That(_employees.Get(_employeeId).BaseSalary, Is.EqualTo(55000m),
                "activating the contract wrote its salary onto the employee");

            // Payroll re-run (still no override) now follows the contract-set salary.
            decimal brutAt55 = RunPayroll();
            Assert.That(brutAt55, Is.GreaterThan(brutAt40));
            Assert.That(brutAt55 / 55000m, Is.EqualTo(brutAt40 / 40000m).Within(0.0001m),
                "gross scales linearly with the contract salary — payroll reads it, never a separate figure");
        }

        private decimal RunPayroll()
        {
            PayrollResult result = _payroll.Preview(new PayrollGenerationRequest
            {
                CompanyId = _companyId, EmployeeId = _employeeId, Year = Year, Month = 7,
                WorkedDays = 26m, WorkableDays = 26m, BaseSalaryOverride = null
            });
            Assert.That(result.IsSuccess, Is.True, "payroll preview succeeds for the shared employee");
            return result.Totals.SalaireBrut;
        }
    }
}
