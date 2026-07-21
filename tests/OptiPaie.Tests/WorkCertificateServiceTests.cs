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
    /// Work-certificates module — integration tests against a real SQLite file. They
    /// prove the auto-numbering and, above all, that the document content is rendered
    /// LIVE from the shared employee/company records — a salary change on the shared
    /// employee is reflected without touching the certificate.
    /// </summary>
    [TestFixture]
    public sealed class WorkCertificateServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private IWorkCertificateService _service;

        private long _companyId;
        private long _employeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-cert-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new WorkCertificateService(_unitOfWorkFactory);

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
                    PaymentMode = PaymentMode.BankTransfer,
                    ContractType = ContractType.Cdi,
                    Poste = "Comptable",
                    HireDate = new DateTime(2021, 3, 1),
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

        private void SetSalary(decimal salary)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee e = uow.Employees.GetById(_employeeId);
                e.BaseSalary = salary;
                uow.Employees.Update(e);
            }
        }

        // ---------------------------------------------------------------- numbering

        [Test]
        public void Save_AutoNumbersByTypeAndYear()
        {
            long a = _service.Save(NewCertificate(CertificateType.WorkCertificate)).Value;
            long b = _service.Save(NewCertificate(CertificateType.WorkCertificate)).Value;
            long c = _service.Save(NewCertificate(CertificateType.SalaryCertificate)).Value;

            int year = DateTime.Today.Year;
            Assert.That(_service.Get(a).Reference, Is.EqualTo("ATT-" + year + "-0001"));
            Assert.That(_service.Get(b).Reference, Is.EqualTo("ATT-" + year + "-0002"), "sequence increments");
            Assert.That(_service.Get(c).Reference, Is.EqualTo("SAL-" + year + "-0003"), "shared per-company yearly counter");
        }

        [Test]
        public void Save_KeepsAnExplicitReference()
        {
            WorkCertificate cert = NewCertificate(CertificateType.WorkCertificate);
            cert.Reference = "REF-CUSTOM";

            long id = _service.Save(cert).Value;

            Assert.That(_service.Get(id).Reference, Is.EqualTo("REF-CUSTOM"));
        }

        [Test]
        public void Save_CustomWithoutBody_IsRejected()
        {
            WorkCertificate cert = NewCertificate(CertificateType.Custom);
            cert.Body = null;

            Assert.That(_service.Save(cert).IsFailure, Is.True);
        }

        [Test]
        public void Save_UnknownEmployee_IsRejected()
        {
            WorkCertificate cert = NewCertificate(CertificateType.WorkCertificate);
            cert.EmployeeId = 999999;

            Assert.That(_service.Save(cert).IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- live render

        [Test]
        public void BuildRender_PullsTheSharedEmployeeAndCompany()
        {
            long id = _service.Save(NewCertificate(CertificateType.WorkCertificate)).Value;

            CertificateRenderModel model = _service.BuildRender(id);

            Assert.That(model, Is.Not.Null);
            Assert.That(model.Employee.LastNameFr, Is.EqualTo("BENALI"));
            Assert.That(model.Company.NameFr, Is.EqualTo("SARL Test"));
            Assert.That(model.MonthlySalary, Is.EqualTo(60000m));
        }

        [Test]
        public void BuildRender_ReflectsALaterSalaryChange_WithoutTouchingTheCertificate()
        {
            long id = _service.Save(NewCertificate(CertificateType.SalaryCertificate)).Value;
            Assert.That(_service.BuildRender(id).MonthlySalary, Is.EqualTo(60000m));

            // The shared employee's salary changes (e.g. a new contract was activated).
            SetSalary(75000m);

            Assert.That(_service.BuildRender(id).MonthlySalary, Is.EqualTo(75000m),
                "the certificate renders live from the shared record");
        }

        [Test]
        public void BuildRender_ComputesSeniorityFromTheSharedHireDate()
        {
            // Hired 2021-03-01; issue the certificate on 2024-09-01 → 3 years 6 months.
            WorkCertificate cert = NewCertificate(CertificateType.WorkCertificate);
            cert.IssueDate = new DateTime(2024, 9, 1);
            long id = _service.Save(cert).Value;

            CertificateRenderModel model = _service.BuildRender(id);

            Assert.That(model.SeniorityYears, Is.EqualTo(3));
            Assert.That(model.SeniorityMonths, Is.EqualTo(6));
        }

        // ---------------------------------------------------------------- listing

        [Test]
        public void GetByCompany_CarriesTheSharedEmployeeName()
        {
            _service.Save(NewCertificate(CertificateType.WorkCertificate));

            IReadOnlyList<CertificateSummary> list = _service.GetByCompany(_companyId);

            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].EmployeeName, Is.EqualTo("BENALI Karim"));
        }

        [Test]
        public void Delete_RemovesTheCertificate()
        {
            long id = _service.Save(NewCertificate(CertificateType.WorkCertificate)).Value;

            Assert.That(_service.Delete(id).IsSuccess, Is.True);
            Assert.That(_service.Get(id), Is.Null);
        }

        private WorkCertificate NewCertificate(CertificateType type)
        {
            return new WorkCertificate
            {
                EmployeeId = _employeeId,
                Type = type,
                IssueDate = DateTime.Today,
                Purpose = "Pour servir et valoir ce que de droit",
                Body = type == CertificateType.Custom ? "Texte libre" : null
            };
        }
    }
}
