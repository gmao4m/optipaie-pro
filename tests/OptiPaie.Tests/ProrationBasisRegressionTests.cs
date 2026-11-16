using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Security;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 2 guard: after adding the work calendar, a company that never touches the setting must
    /// still get today's proration — base ÷ the month's real days. The owner's real payslips are all
    /// in June (30 days), which would HIDE a ÷30-vs-÷31 regression, so this proves it on a synthetic
    /// 31-day month through the REAL payroll path. It also pins the default for Commit 4.
    /// </summary>
    [TestFixture]
    public sealed class ProrationBasisRegressionTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-prorata-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void Default_31DayMonth_With_3_Absences_Still_Divides_By_31()
        {
            var companies = new CompanyService(_uow, new CompanyValidator(), AccessContext.Unrestricted);
            var employees = new EmployeeService(_uow, new EmployeeValidator(), AccessContext.Unrestricted);
            var attendance = new AttendanceService(_uow, AccessContext.Unrestricted);
            var config = new ConfigurationService(_uow);
            var payroll = new PayrollService(_uow, config, new PayrollCalculationEngine(), AccessContext.Unrestricted);
            var batch = new BatchPayrollService(
                employees, new PayrollElementService(_uow, new PayrollElementValidator(), AccessContext.Unrestricted),
                new LoanService(_uow, AccessContext.Unrestricted), attendance, payroll,
                new ContractService(_uow, AccessContext.Unrestricted), new ArchiveService(_uow), _ => true, AccessContext.Unrestricted);

            long companyId = companies.Create(new Company { NameFr = "SARL 31", Nif = "000000000000000" }).Value;
            long emp = employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "JUILLET", FirstNameFr = "Test", Poste = "Agent",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2024, 1, 1), BaseSalary = 31000m, IsActive = true
            }).Value;

            // 3 unjustified absences in July 2026 (a 31-day month).
            attendance.SetDayStatusBulk(new List<AttendanceDayStatus>
            {
                new AttendanceDayStatus(emp, new DateTime(2026, 7, 6), AttendanceStatus.Absent),
                new AttendanceDayStatus(emp, new DateTime(2026, 7, 7), AttendanceStatus.Absent),
                new AttendanceDayStatus(emp, new DateTime(2026, 7, 8), AttendanceStatus.Absent),
            });

            PayrollGenerationRequest request = batch.BuildRequest(companyId, emp, 2026, 7);
            // Today's rule: WorkableDays = calendar month days (31), WorkedDays = 31 - 3 = 28.
            Assert.That(request.WorkableDays, Is.EqualTo(31m), "denominator = month's real days");
            Assert.That(request.WorkedDays, Is.EqualTo(28m));

            PayrollResult result = payroll.Preview(request);
            // base prorated = 31000 × 28/31 = 28000.00 exactly (÷31, NOT ÷30 which would give 27900).
            Assert.That(result.Totals.SalaireBrut, Is.EqualTo(28000m));
        }
    }
}
