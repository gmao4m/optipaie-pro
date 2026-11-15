using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Documents;
using OptiPaie.Services.ImportExport;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// COMMIT F — the salary exposure sweep. One permission (<see cref="Permission.ViewSalary"/>)
    /// governs seeing an identified employee's pay through ANY door. This test seeds an employee with
    /// a DISTINCTIVE salary (its digits are <see cref="SalaryDigits"/>) and, for a read-only role,
    /// walks every salary-bearing surface and asserts the figure is ABSENT from the actual produced
    /// content — report cells, the exported file's bytes, and the exact salary string each document
    /// draws. A positive control proves the SAME surface DOES carry the figure for a role that may see
    /// pay, so the test proves something about what the customer receives, not a boolean. The
    /// reflective report loop makes it fail when a NEW salary-bearing report is added later.
    /// </summary>
    [TestFixture]
    public sealed class SalaryExposureSweepTests
    {
        private const decimal Sentinel = 918273.64m;   // a salary no other value in the fixture equals
        private const string SalaryDigits = "91827364"; // its digits, separator-free, to search for

        private string _dir;
        private SqliteConnectionFactory _factory;
        private UnitOfWorkFactory _uow;
        private long _companyId;
        private long _employeeId;
        private long _certificateId;

        private static readonly IAccessContext ReadOnly =
            new Ctx { IsEnforced = true, Role = UserRole.ReadOnly };
        private static readonly IAccessContext Payroll =
            new Ctx { IsEnforced = true, Role = UserRole.Payroll };

        [SetUp]
        public void SetUp()
        {
            SqliteTypeHandlers.Register();
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-salary-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "s.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(_factory);

            using (IUnitOfWork uow = _uow.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId,
                    LastNameFr = "Benali", FirstNameFr = "Karim",
                    Gender = Gender.Male,
                    HireDate = new DateTime(2020, 1, 1),
                    BaseSalary = Sentinel,
                    ContractType = ContractType.Cdi,
                    MaritalStatus = MaritalStatus.Single,
                    PaymentMode = PaymentMode.BankTransfer,
                    Poste = "Comptable",
                    IsActive = true
                });
                uow.Contracts.Insert(new EmploymentContract
                {
                    EmployeeId = _employeeId, Type = ContractType.Cdi, Status = ContractStatus.Active,
                    StartDate = new DateTime(2020, 1, 1), BaseSalary = Sentinel
                });
                _certificateId = uow.Certificates.Insert(new WorkCertificate
                {
                    EmployeeId = _employeeId, Type = CertificateType.SalaryCertificate,
                    IssueDate = new DateTime(2024, 6, 1), Reference = "C-1"
                });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ---- the sweep: every surface, read-only sees no figure; a payroll role does ----

        [Test]
        public void Surface_AccessPolicy_SalaryGate()
        {
            Assert.That(AccessPolicy.CanSeeSalary(UserRole.Admin), Is.True);
            Assert.That(AccessPolicy.CanSeeSalary(UserRole.Hr), Is.True);
            Assert.That(AccessPolicy.CanSeeSalary(UserRole.Payroll), Is.True);
            Assert.That(AccessPolicy.CanSeeSalary(UserRole.Accountant), Is.True);
            Assert.That(AccessPolicy.CanSeeSalary(UserRole.ReadOnly), Is.False, "read-only may never see pay");
        }

        [Test]
        public void Surface_HeadcountReport_RosterCells()
        {
            Assert.That(ContainsSalary(ReportCells(ReadOnly, ReportService.Headcount)), Is.False,
                "read-only: the roster report must carry no salary");
            Assert.That(ContainsSalary(ReportCells(Payroll, ReportService.Headcount)), Is.True,
                "positive control: payroll DOES see the salary (so the test is meaningful)");
        }

        [Test]
        public void Surface_EveryReportDescriptor_CarriesNoSalary_ForReadOnly()
        {
            // Rot-proof: a NEW report that exposes salary without the gate fails here automatically.
            IReportService reports = Reports(ReadOnly);
            foreach (ReportDescriptor d in reports.GetReports(_companyId))
            {
                string cells = Flatten(reports.Build(d.Key, _companyId, 2024, 6));
                Assert.That(ContainsSalary(cells), Is.False, "read-only report '" + d.Key + "' leaked a salary");
            }
        }

        [Test]
        public void Surface_EmployeeExport_FileBytes()
        {
            string roFile = Path.Combine(_dir, "ro.csv");
            Export(ReadOnly).Export(_companyId, roFile, ImportFileFormat.Csv);
            Assert.That(ContainsSalary(File.ReadAllText(roFile)), Is.False,
                "read-only: the exported employee file must carry no salary");

            string payFile = Path.Combine(_dir, "pay.csv");
            Export(Payroll).Export(_companyId, payFile, ImportFileFormat.Csv);
            Assert.That(ContainsSalary(File.ReadAllText(payFile)), Is.True,
                "positive control: payroll's export DOES carry the salary");
        }

        [Test]
        public void Surface_WorkCertificate_SalaryField()
        {
            CertificateRenderModel ro = Certificates(ReadOnly).BuildRender(_certificateId);
            Assert.That(ContainsSalary(WorkCertificateDocument.SalaryText(ro)), Is.False,
                "read-only: the salary certificate must be produced WITHOUT the figure");
            Assert.That(WorkCertificateDocument.SalaryText(ro), Is.EqualTo(SalaryVisibility.Masked));

            CertificateRenderModel pay = Certificates(Payroll).BuildRender(_certificateId);
            Assert.That(ContainsSalary(WorkCertificateDocument.SalaryText(pay)), Is.True,
                "positive control: payroll's salary certificate carries the figure");
        }

        [Test]
        public void Surface_LeaveSettlement_Figures()
        {
            FinalSettlement ro = Leave(ReadOnly).ComputeFinalSettlement(_employeeId, new DateTime(2024, 6, 30));
            Assert.That(ContainsSalary(LeaveSettlementDocument.PayText(ro.MonthlySalary, ro.ShowSalary)), Is.False,
                "read-only: the leave settlement must carry no salary");
            Assert.That(LeaveSettlementDocument.PayText(ro.MonthlySalary, ro.ShowSalary), Is.EqualTo(SalaryVisibility.Masked));

            FinalSettlement pay = Leave(Payroll).ComputeFinalSettlement(_employeeId, new DateTime(2024, 6, 30));
            Assert.That(ContainsSalary(LeaveSettlementDocument.PayText(pay.MonthlySalary, pay.ShowSalary)), Is.True,
                "positive control: payroll's leave settlement carries the salary");
        }

        [Test]
        public void Surface_ContractList_SalaryText()
        {
            // The contract-list salary cell routes through the same gate (ContractSummary.BaseSalary).
            Assert.That(ContainsSalary(SalaryVisibility.Text(Sentinel, ReadOnly, System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))), Is.False);
            Assert.That(ContainsSalary(SalaryVisibility.Text(Sentinel, Payroll, System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))), Is.True);
        }

        // ---- surface builders ----

        private string ReportCells(IAccessContext ctx, string key) => Flatten(Reports(ctx).Build(key, _companyId, 2024, 6));

        private static string Flatten(ReportTable table)
        {
            if (table?.Rows == null) return string.Empty;
            return string.Join("|", table.Rows.SelectMany(r => r));
        }

        private IReportService Reports(IAccessContext ctx)
        {
            var companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            var employees = new EmployeeService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            var attendance = new AttendanceService(_uow, AccessContext.Unrestricted);
            var leave = new LeaveService(_uow, AccessContext.Unrestricted);
            var loans = new LoanService(_uow, AccessContext.Unrestricted);
            var ats = new AtsService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            var assets = new AssetService(_uow, AccessContext.Unrestricted);
            var training = new TrainingService(_uow, AccessContext.Unrestricted);
            return new ReportService(companies, employees, attendance, leave, loans, ats, assets, training, ctx);
        }

        private EmployeeImportExportService Export(IAccessContext ctx) =>
            new EmployeeImportExportService(_uow, new EmployeeValidator(), ctx);

        private WorkCertificateService Certificates(IAccessContext ctx) => new WorkCertificateService(_uow, ctx);

        private LeaveService Leave(IAccessContext ctx) => new LeaveService(_uow, ctx);

        // ---- separator-free salary search (catches any number format) ----

        private static bool ContainsSalary(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text) if (char.IsDigit(c)) sb.Append(c);
            return sb.ToString().Contains(SalaryDigits);
        }

        private sealed class Ctx : IAccessContext
        {
            public bool IsEnforced { get; set; }
            public UserRole Role { get; set; }
            public UserRole CurrentRole => Role;
            public bool IsOwner => false;
            public long? CurrentUserId => null;
        }
    }
}
