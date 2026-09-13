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
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;
using Cert = OptiPaie.Core.Certificates;

namespace OptiPaie.Tests
{
    /// <summary>
    /// ATS page-2 salary table (defect 2). Proves the reference table is filled from the employee's
    /// ACTUAL payroll history — the SAME persisted contributable base (<c>Payslip.BaseCotisable</c>)
    /// the CNAS declaration reads — so the "/" fallback is never reached for a month that has payroll,
    /// and only genuinely-unused rows are struck with "/".
    /// </summary>
    [TestFixture]
    public sealed class AtsContributionHistoryTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private ContractService _contracts;
        private ArchiveService _archive;
        private ConfigurationService _config;
        private PayrollService _payroll;
        private BatchPayrollService _batch;
        private CnasDeclarationService _cnas;
        private AtsDrtDocumentService _ats;

        private static readonly int Year = DateTime.Today.Year - 1; // a full year in the past

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-atshist-" + Guid.NewGuid().ToString("N"));
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
            _batch = new BatchPayrollService(
                _employees, new PayrollElementService(_uow, new PayrollElementValidator()),
                new LoanService(_uow), new AttendanceService(_uow), _payroll, _contracts, _archive, _ => true);
            _cnas = new CnasDeclarationService(_companies, _employees, _archive, _config);
            _ats = new AtsDrtDocumentService(_companies, _employees, _archive);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private long AddCompany() =>
            _companies.Create(new Company { NameFr = "SARL ATS", Nif = "000000000000000", CnasEmployerNumber = "1234567890" }).Value;

        private long AddEmployee(long companyId, DateTime hire) =>
            _employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "BENALI", FirstNameFr = "Karim", Poste = "Agent",
                Nss = "123456789012", BirthDate = new DateTime(1985, 1, 1), Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash, ContractType = ContractType.Cdi,
                HireDate = hire, BaseSalary = 50000m, IsActive = true
            }).Value;

        private decimal PayslipBase(long companyId, long employeeId, int month)
        {
            PayrollRun run = _archive.SearchRuns(companyId, Year, month).First(r => r.CompanyId == companyId);
            PayrollRun loaded = _archive.GetRun(run.Id);
            return loaded.Payslips.Single(p => p.EmployeeId == employeeId).BaseCotisable;
        }

        [Test]
        public void PageTwo_RowsCarryTheSamePersistedBaseAsTheCnasDeclaration()
        {
            long companyId = AddCompany();
            long empId = AddEmployee(companyId, new DateTime(Year - 2, 1, 1));
            var months = new[] { 3, 4, 5, 6 };
            foreach (int m in months) _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, m));

            // Window covers months 3..8; only 3..6 have payroll → exactly 4 active rows.
            List<Cert.MonthlyContribution> rows =
                _ats.BuildContributionsFromPayroll(companyId, empId, new DateTime(Year, 3, 1), 6, false);
            List<Cert.MonthlyContribution> active = rows.Where(r => r.IsActive).ToList();

            Assert.That(active.Count, Is.EqualTo(4), "one active row per month that actually has a payslip");

            for (int i = 0; i < months.Length; i++)
            {
                decimal persisted = PayslipBase(companyId, empId, months[i]);
                decimal dacAssiette = _cnas.BuildDac(companyId, Year, new[] { months[i] }).Assiette;

                Assert.That(active[i].ContributionBase, Is.EqualTo(persisted),
                    "ATS contribution base = the persisted Payslip.BaseCotisable (never recomputed)");
                Assert.That(active[i].ContributionBase, Is.EqualTo(dacAssiette),
                    "ATS base for a month EQUALS the CNAS DAC assiette for the same employee and month");
                // Part ouvrière = 9% of the base (the confirmed CNAS employee rate).
                Assert.That(active[i].EmployeeShare, Is.EqualTo(Math.Round(persisted * 0.09m, 2)));
            }
        }

        [Test]
        public void SlashFallback_IsNeverReached_ForAMonthThatHasPayroll()
        {
            long companyId = AddCompany();
            long empId = AddEmployee(companyId, new DateTime(Year - 2, 1, 1));
            var months = new[] { 1, 2, 3, 4, 5, 6 };
            foreach (int m in months) _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, m));

            List<Cert.MonthlyContribution> rows =
                _ats.BuildContributionsFromPayroll(companyId, empId, new DateTime(Year, 1, 1), 6, false);

            // Every ACTIVE (labelled) row carries a real base + worked days — the mapper prints values,
            // never the "/" fallback. Only the padding rows are inactive (rendered "/").
            List<Cert.MonthlyContribution> active = rows.Where(r => r.IsActive).ToList();
            Assert.That(active.Count, Is.EqualTo(6));
            Assert.That(active.All(r => r.ContributionBase.HasValue && r.ContributionBase.Value > 0m), Is.True,
                "a labelled reference month always carries a real contribution base — never '/'");
            Assert.That(active.All(r => r.DaysWorked.HasValue), Is.True, "worked days come from payroll");

            // Render the field dictionary and confirm no active data cell is the "/" fallback.
            var data = new Cert.CertificateService(new Cert.WeekendConfig())
                .BuildAts(new OptiPaie.Core.Certificates.Company(), new OptiPaie.Core.Certificates.Employee(),
                          new Cert.WorkStoppage { StoppageDate = DateTime.Today, NumberOfDays = 0 }, false, rows);
            Dictionary<string, string> v = Cert.CertificateBookmarkMapper.MapAts(data);
            for (int n = 1; n <= 6; n++)
            {
                Assert.That(v["SS" + n], Is.Not.EqualTo("/"), "salaire soumis à cotisations is real data, not '/'");
                Assert.That(v["PO" + n], Is.Not.EqualTo("/"), "part ouvrière is real data, not '/'");
                Assert.That(v["JT" + n], Is.Not.EqualTo("/"), "jours travaillés is real data, not '/'");
            }
        }

        [Test]
        public void HiredMidWindow_MonthsBeforeHire_AreNotActive_AndDoNotShowAsData()
        {
            long companyId = AddCompany();
            // Hired in April of the reference year — no payroll before April.
            long empId = AddEmployee(companyId, new DateTime(Year, 4, 1));
            foreach (int m in new[] { 4, 5, 6 }) _payroll.Generate(_batch.BuildRequest(companyId, empId, Year, m));

            List<Cert.MonthlyContribution> rows =
                _ats.BuildContributionsFromPayroll(companyId, empId, new DateTime(Year, 1, 1), 12, false);
            List<Cert.MonthlyContribution> active = rows.Where(r => r.IsActive).ToList();

            Assert.That(active.Count, Is.EqualTo(3), "only the 3 months since hire have payroll");
            Assert.That(active.All(r => r.ContributionBase.HasValue), Is.True);
            // Jan–Mar (before hire) never become active rows, so they render as "/" (not applicable),
            // never as a blank/"failed" data cell for a labelled month.
            Assert.That(rows.Count(r => !r.IsActive), Is.EqualTo(9));
        }

        [Test]
        public void EmptyHistory_ProducesNoActiveRows_NeverThrows()
        {
            long companyId = AddCompany();
            long empId = AddEmployee(companyId, new DateTime(Year - 2, 1, 1)); // no payroll generated

            List<Cert.MonthlyContribution> rows =
                _ats.BuildContributionsFromPayroll(companyId, empId, new DateTime(Year, 1, 1), 12, false);
            Assert.That(rows.Count(r => r.IsActive), Is.EqualTo(0), "no payroll → no active rows (all struck '/')");
            Assert.That(rows.Count, Is.EqualTo(12));
        }
    }
}
