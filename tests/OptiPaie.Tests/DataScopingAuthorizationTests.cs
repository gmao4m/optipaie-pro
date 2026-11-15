using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// COMMIT C — close the data, not just the door. Proves the corrected matrix at every layer:
    /// the view/export rules (C.1/C.2), the archive and reports "back doors" scoped by role (C.3),
    /// and that admins cannot mint admins and no account changes its own role (C.4).
    /// </summary>
    [TestFixture]
    public sealed class DataScopingAuthorizationTests
    {
        private string _dir;
        private SqliteConnectionFactory _factory;
        private UnitOfWorkFactory _uow;

        [SetUp]
        public void SetUp()
        {
            SqliteTypeHandlers.Register();
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-scoping-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "s.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(_factory);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ---------------------------------------------------------------- C.1 / C.2 : the matrix

        [Test]
        public void Payroll_IsViewableByAllButReadOnly_AndExportableByAdminPayrollAccountantOnly()
        {
            Assert.That(AccessPolicy.CanOpen(UserRole.Admin, "payroll"), Is.True);
            Assert.That(AccessPolicy.CanOpen(UserRole.Hr, "payroll"), Is.True, "HR keeps payroll VIEW");
            Assert.That(AccessPolicy.CanOpen(UserRole.Payroll, "payroll"), Is.True);
            Assert.That(AccessPolicy.CanOpen(UserRole.Accountant, "payroll"), Is.True);
            Assert.That(AccessPolicy.CanOpen(UserRole.ReadOnly, "payroll"), Is.False, "read-only loses payroll entirely");

            Assert.That(AccessPolicy.CanExportPayroll(UserRole.Admin), Is.True);
            Assert.That(AccessPolicy.CanExportPayroll(UserRole.Payroll), Is.True);
            Assert.That(AccessPolicy.CanExportPayroll(UserRole.Accountant), Is.True);
            Assert.That(AccessPolicy.CanExportPayroll(UserRole.Hr), Is.False, "HR views but cannot export salary");
            Assert.That(AccessPolicy.CanExportPayroll(UserRole.ReadOnly), Is.False);
        }

        [Test]
        public void Cnas_IsPayrollAndAccountantOnly_ForBothViewAndExport()
        {
            Assert.That(AccessPolicy.CanOpen(UserRole.Admin, "cnas"), Is.True);
            Assert.That(AccessPolicy.CanOpen(UserRole.Payroll, "cnas"), Is.True);
            Assert.That(AccessPolicy.CanOpen(UserRole.Accountant, "cnas"), Is.True);
            Assert.That(AccessPolicy.CanOpen(UserRole.Hr, "cnas"), Is.False);
            Assert.That(AccessPolicy.CanOpen(UserRole.ReadOnly, "cnas"), Is.False, "read-only loses CNAS entirely");

            Assert.That(AccessPolicy.CanExportCnas(UserRole.Hr), Is.False);
            Assert.That(AccessPolicy.CanExportCnas(UserRole.ReadOnly), Is.False);
            Assert.That(AccessPolicy.CanExportCnas(UserRole.Accountant), Is.True);
        }

        // ---------------------------------------------------------------- C.3 : the archive back door

        [Test]
        public void Archive_ReadOnly_SeesZeroPayslips_AndCannotPullTheDocument()
        {
            long companyId = SeedCompany();
            long employeeId = SeedEmployee(companyId);
            SeedRunWithPayslip(companyId, employeeId, out long payslipId);
            SeedArchiveDocument(payslipId);

            var readOnly = new ArchiveService(_uow, Ctx(UserRole.ReadOnly));
            Assert.That(readOnly.SearchRuns(companyId, 2024, 6), Is.Empty, "read-only must see zero runs");
            Assert.That(readOnly.GetPayslipsByEmployee(employeeId), Is.Empty, "read-only must see zero payslips");
            Assert.That(readOnly.GetPayslip(payslipId), Is.Null);
            Assert.That(readOnly.GetDocument(payslipId, "fr"), Is.Null, "read-only can export nothing");

            // HR may VIEW the list but not pull the export document.
            var hr = new ArchiveService(_uow, Ctx(UserRole.Hr));
            Assert.That(hr.GetPayslipsByEmployee(employeeId), Is.Not.Empty, "HR may view the archive");
            Assert.That(hr.GetDocument(payslipId, "fr"), Is.Null, "HR may view but not export a payslip");

            // Payroll sees and exports everything.
            var payroll = new ArchiveService(_uow, Ctx(UserRole.Payroll));
            Assert.That(payroll.GetPayslipsByEmployee(employeeId), Is.Not.Empty);
            Assert.That(payroll.GetDocument(payslipId, "fr"), Is.Not.Null);
        }

        // ---------------------------------------------------------------- C.3 : the reports back door

        [Test]
        public void Reports_ReadOnly_GetsZeroPayrollReports_ButPayrollRoleDoes()
        {
            long companyId = SeedCompany(btph: true);

            IReportService readOnly = BuildReportService(Ctx(UserRole.ReadOnly));
            var roKeys = readOnly.GetReports(companyId).Select(r => r.Key).ToList();
            Assert.That(roKeys, Does.Not.Contain(ReportService.DasCacobatph), "read-only gets no payroll (salary) report");
            Assert.That(roKeys, Does.Not.Contain(ReportService.DacCacobatph));
            Assert.That(roKeys, Does.Contain(ReportService.Headcount), "but still gets the HR reports it may view");
            // Building a denied report yields no data.
            Assert.That(readOnly.Build(ReportService.DacCacobatph, companyId, 2024, 1).Rows, Is.Empty);
            Assert.That(readOnly.Build(ReportService.DacCacobatph, companyId, 2024, 1).Title, Does.Contain("refusé"));

            IReportService payroll = BuildReportService(Ctx(UserRole.Payroll));
            var payKeys = payroll.GetReports(companyId).Select(r => r.Key).ToList();
            Assert.That(payKeys, Does.Contain(ReportService.DasCacobatph), "payroll sees the salary declarations");
            Assert.That(payKeys, Does.Contain(ReportService.DacCacobatph));
        }

        // ---------------------------------------------------------------- C.3 / C.2 : CNAS files

        [Test]
        public void Cnas_BuildAndExport_AreDeniedToReadOnlyAndHr_AllowedToPayroll()
        {
            long companyId = SeedCompany();
            ICnasDeclarationService readOnly = BuildCnasService(Ctx(UserRole.ReadOnly));
            Assert.Throws<AccessDeniedException>(() => readOnly.BuildDas(companyId, 2024));
            Assert.Throws<AccessDeniedException>(() => readOnly.ValidateDasForExport(companyId, 2024));

            ICnasDeclarationService hr = BuildCnasService(Ctx(UserRole.Hr));
            Assert.Throws<AccessDeniedException>(() => hr.BuildDas(companyId, 2024));

            // Payroll may build/validate (no AccessDeniedException; data correctness is tested elsewhere).
            ICnasDeclarationService payroll = BuildCnasService(Ctx(UserRole.Payroll));
            Assert.DoesNotThrow(() => payroll.BuildDas(companyId, 2024));
        }

        // ---------------------------------------------------------------- C.4 : admins cannot mint admins

        [Test]
        public void Admin_CannotCreateAnAdmin_ButCanCreateNonAdmin_OwnerCan()
        {
            var admin = new UserService(_uow, new SettingsService(_uow), AdminCtx(999));

            Result<long> asAdmin = admin.Create("newadmin", "New Admin", "password-1234", UserRole.Admin, null);
            Assert.That(asAdmin.IsSuccess, Is.False, "an administrator may not create an administrator");
            Assert.That(asAdmin.ErrorCode, Is.EqualTo("User_AdminOwnerOnly"));

            Result<long> nonAdmin = admin.Create("newhr", "New HR", "password-1234", UserRole.Hr, null);
            Assert.That(nonAdmin.IsSuccess, Is.True, "an administrator may create a non-admin role");

            var owner = new UserService(_uow, new SettingsService(_uow), OwnerCtx());
            Assert.That(owner.Create("admin2", "Second Admin", "password-1234", UserRole.Admin, null).IsSuccess, Is.True,
                "only the owner may create an administrator");
        }

        [Test]
        public void Admin_CannotElevateAnAccountToAdmin_OwnerCan()
        {
            // Seed a non-admin user (created by the owner so the Create is allowed).
            var owner = new UserService(_uow, new SettingsService(_uow), OwnerCtx());
            long hrId = owner.Create("payroll1", "Payroll One", "password-1234", UserRole.Payroll, null).Value;

            var admin = new UserService(_uow, new SettingsService(_uow), AdminCtx(999));
            Result elevate = admin.Update(new User { Id = hrId, Role = UserRole.Admin, IsActive = true, FullName = "Payroll One" });
            Assert.That(elevate.IsSuccess, Is.False, "an administrator may not elevate an account to admin");
            Assert.That(elevate.ErrorCode, Is.EqualTo("User_AdminOwnerOnly"));

            Assert.That(owner.Update(new User { Id = hrId, Role = UserRole.Admin, IsActive = true, FullName = "Payroll One" }).IsSuccess,
                Is.True, "the owner may elevate to admin");
        }

        [Test]
        public void NoAccount_MayChangeItsOwnRole()
        {
            var owner = new UserService(_uow, new SettingsService(_uow), OwnerCtx());
            long meId = owner.Create("me", "Me", "password-1234", UserRole.Payroll, null).Value;

            // The signed-in account is 'me' (CurrentUserId = meId) and tries to change its own role.
            var me = new UserService(_uow, new SettingsService(_uow), AdminCtx(meId));
            Result selfChange = me.Update(new User { Id = meId, Role = UserRole.Admin, IsActive = true, FullName = "Me" });
            Assert.That(selfChange.IsSuccess, Is.False, "an account may never change its own role");
            Assert.That(selfChange.ErrorCode, Is.EqualTo("User_SelfRoleChange"));
        }

        // ---------------------------------------------------------------- helpers

        private IReportService BuildReportService(IAccessContext ctx)
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

        private ICnasDeclarationService BuildCnasService(IAccessContext ctx)
        {
            var companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            var employees = new EmployeeService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            var archive = new ArchiveService(_uow, AccessContext.Unrestricted);
            var config = new ConfigurationService(_uow);
            return new CnasDeclarationService(companies, employees, archive, config, ctx);
        }

        private long SeedCompany(bool btph = false)
        {
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            using (SQLiteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO Companies (NameFr, CnasEmployerNumber, BtphSector, CacobatphEnabled, CreatedAtUtc) " +
                    "VALUES ('SARL Test', '1234567890', @btph, @caco, '2024-01-01 00:00:00'); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@btph", btph ? 1 : 0);
                cmd.Parameters.AddWithValue("@caco", btph ? 1 : 0);
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private long SeedEmployee(long companyId)
        {
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            using (SQLiteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO Employees (CompanyId, LastNameFr, FirstNameFr, HireDate, BaseSalary, CreatedAtUtc) " +
                    "VALUES (@cid, 'Nom', 'Prenom', '2023-01-01', '60000', '2024-01-01 00:00:00'); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@cid", companyId);
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private void SeedRunWithPayslip(long companyId, long employeeId, out long payslipId)
        {
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            {
                Exec(c, "INSERT INTO PayrollRuns (CompanyId, PeriodYear, PeriodMonth, RunStatus, EngineVersion, CreatedAtUtc) " +
                        "VALUES (" + companyId + ", 2024, 6, 2, '1.0', '2024-06-30 00:00:00');");
                using (SQLiteCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO Payslips (RunId, EmployeeId, BaseCotisable, NetSalaire, GeneratedAtUtc, CreatedAtUtc) " +
                        "VALUES ((SELECT Id FROM PayrollRuns WHERE CompanyId=" + companyId + "), @eid, '60000', '55000', " +
                        "'2024-06-30 00:00:00', '2024-06-30 00:00:00'); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@eid", employeeId);
                    payslipId = Convert.ToInt64(cmd.ExecuteScalar());
                }
            }
        }

        private void SeedArchiveDocument(long payslipId)
        {
            using (SQLiteConnection c = _factory.CreateOpenConnection())
            using (SQLiteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO ArchiveDocuments (PayslipId, LanguageCode, SnapshotJson, CreatedAtUtc) " +
                    "VALUES (@pid, 'fr', '{}', '2024-06-30 00:00:00');";
                cmd.Parameters.AddWithValue("@pid", payslipId);
                cmd.ExecuteNonQuery();
            }
        }

        private static void Exec(SQLiteConnection c, string sql)
        {
            using (SQLiteCommand cmd = c.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }

        private static IAccessContext Ctx(UserRole role) =>
            new Access { IsEnforced = true, CurrentRole = role, IsOwner = false, CurrentUserId = null };

        private static IAccessContext AdminCtx(long userId) =>
            new Access { IsEnforced = true, CurrentRole = UserRole.Admin, IsOwner = false, CurrentUserId = userId };

        private static IAccessContext OwnerCtx() =>
            new Access { IsEnforced = true, CurrentRole = UserRole.Admin, IsOwner = true, CurrentUserId = null };

        private sealed class Access : IAccessContext
        {
            public bool IsEnforced { get; set; }
            public UserRole CurrentRole { get; set; }
            public bool IsOwner { get; set; }
            public long? CurrentUserId { get; set; }
        }
    }
}
