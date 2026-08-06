using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;
using Cert = OptiPaie.Core.Certificates;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Proves the 4 new identity fields (migration 0029) persist through the real repositories
    /// and are auto-mapped into the ATS/DRT certificate snapshot for auto-fill.
    /// </summary>
    [TestFixture]
    public sealed class AtsDrtIdentityFieldsTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-atsid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        [Test]
        public void NewIdentityFields_RoundTrip_AndFeedTheCertificateSnapshot()
        {
            long companyId = _companies.Create(new Company
            {
                NameFr = "SARL Atlas Industrie", Nif = "000000000000000", CnasEmployerNumber = "0910245789",
                AddressFr = "Zone Industrielle, Rouiba", ManagerName = "CHERIF Mohamed", City = "Alger"
            }).Value;

            long empId = _employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = "BENALI", FirstNameFr = "Karim",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2018, 3, 1), BaseSalary = 40000m, IsActive = true,
                Nss = "123456789012", BirthDate = new DateTime(1990, 5, 12), BirthPlace = "Alger",
                Address = "Cité 200 logements, Rouiba", Poste = "Technicien"
            }).Value;

            // Persisted through the real repositories (SELECT * auto-maps the new columns).
            Company c = _companies.Get(companyId);
            Assert.That(c.ManagerName, Is.EqualTo("CHERIF Mohamed"));
            Assert.That(c.City, Is.EqualTo("Alger"));

            Employee e = _employees.Get(empId);
            Assert.That(e.BirthPlace, Is.EqualTo("Alger"));
            Assert.That(e.Address, Is.EqualTo("Cité 200 logements, Rouiba"));

            // The bridge auto-fills the certificate snapshot from the stored files.
            var docs = new AtsDrtDocumentService(_companies, _employees);

            Cert.Company cc = docs.MapCompany(companyId);
            Assert.That(cc.ManagerName, Is.EqualTo("CHERIF Mohamed"));
            Assert.That(cc.Location, Is.EqualTo("Alger"));
            Assert.That(cc.EmployerNumber, Is.EqualTo("0910245789"));
            Assert.That(cc.CompanyName, Is.EqualTo("SARL Atlas Industrie"));

            Cert.Employee ce = docs.MapEmployee(empId);
            Assert.That(ce.BirthPlace, Is.EqualTo("Alger"));
            Assert.That(ce.Address, Is.EqualTo("Cité 200 logements, Rouiba"));
            Assert.That(ce.FullName, Is.EqualTo("BENALI Karim"));
            Assert.That(ce.FormattedNss, Is.EqualTo("12 3456 7890 12"));
            Assert.That(ce.Position, Is.EqualTo("Technicien"));
        }
    }
}
