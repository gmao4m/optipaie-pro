using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Departments module — integration tests against a real SQLite file. They prove the
    /// lazy default seeding (standard set + names already on employees), the per-company
    /// uniqueness rule, and the soft-delete behaviour that leaves employee values intact.
    /// </summary>
    [TestFixture]
    public sealed class DepartmentServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private IDepartmentService _service;

        private long _companyId;
        private long _otherCompanyId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-depts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new DepartmentService(_unitOfWorkFactory);

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _otherCompanyId = uow.Companies.Insert(new Company { NameFr = "EURL Autre", Nif = "111111111111111" });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        private long AddEmployee(long companyId, string department)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                long id = uow.Employees.Insert(new Employee
                {
                    CompanyId = companyId,
                    LastNameFr = "TEST",
                    FirstNameFr = "Employe",
                    Gender = Gender.Male,
                    MaritalStatus = MaritalStatus.Single,
                    PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi,
                    HireDate = new DateTime(2020, 1, 1),
                    BaseSalary = 50000m,
                    IsActive = true,
                    Department = department
                });
                uow.Commit();
                return id;
            }
        }

        // ---------------------------------------------------------------- seeding

        [Test]
        public void GetForCompany_WhenEmpty_SeedsTheDefaultSet()
        {
            IReadOnlyList<Department> departments = _service.GetForCompany(_companyId);

            Assert.That(departments.Select(d => d.Name), Does.Contain("Production"));
            Assert.That(departments.Select(d => d.Name), Does.Contain("Commercial"));
            Assert.That(departments.Select(d => d.Name), Does.Contain("Générale"));
        }

        [Test]
        public void GetForCompany_FoldsInExistingEmployeeDepartments()
        {
            AddEmployee(_companyId, "Logistique");

            IReadOnlyList<string> names = _service.GetNamesForCompany(_companyId);

            Assert.That(names, Does.Contain("Logistique"));
            Assert.That(names, Does.Contain("Production"));
        }

        [Test]
        public void GetForCompany_DoesNotDuplicateWhenEmployeeMatchesDefault()
        {
            AddEmployee(_companyId, "Production");

            IReadOnlyList<string> names = _service.GetNamesForCompany(_companyId);

            Assert.That(names.Count(n => string.Equals(n, "Production", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
        }

        [Test]
        public void GetForCompany_SeedsOnlyOnce()
        {
            IReadOnlyList<Department> first = _service.GetForCompany(_companyId);
            IReadOnlyList<Department> second = _service.GetForCompany(_companyId);

            Assert.That(second.Count, Is.EqualTo(first.Count));
        }

        [Test]
        public void GetForCompany_IsScopedPerCompany()
        {
            _service.Save(new Department { CompanyId = _companyId, Name = "SoloDept" });

            IReadOnlyList<string> other = _service.GetNamesForCompany(_otherCompanyId);

            Assert.That(other, Does.Not.Contain("SoloDept"));
        }

        // ---------------------------------------------------------------- save

        [Test]
        public void Save_NewDepartment_AppearsInTheList()
        {
            long id = _service.Save(new Department { CompanyId = _companyId, Name = "Maintenance" }).Value;

            Assert.That(id, Is.GreaterThan(0));
            Assert.That(_service.GetNamesForCompany(_companyId), Does.Contain("Maintenance"));
        }

        [Test]
        public void Save_WithoutName_IsRejected()
        {
            var result = _service.Save(new Department { CompanyId = _companyId, Name = "  " });
            Assert.That(result.IsFailure, Is.True);
        }

        [Test]
        public void Save_DuplicateName_IsRejected()
        {
            _service.Save(new Department { CompanyId = _companyId, Name = "Qualité" });
            var again = _service.Save(new Department { CompanyId = _companyId, Name = "qualité" });
            Assert.That(again.IsFailure, Is.True);
        }

        [Test]
        public void Save_ExistingDepartment_RenamesIt()
        {
            long id = _service.Save(new Department { CompanyId = _companyId, Name = "Ancien" }).Value;
            _service.Save(new Department { Id = id, CompanyId = _companyId, Name = "Nouveau" });

            IReadOnlyList<string> names = _service.GetNamesForCompany(_companyId);
            Assert.That(names, Does.Contain("Nouveau"));
            Assert.That(names, Does.Not.Contain("Ancien"));
        }

        // ---------------------------------------------------------------- remove

        [Test]
        public void Remove_TakesTheDepartmentOutOfTheList()
        {
            long id = _service.Save(new Department { CompanyId = _companyId, Name = "Temporaire" }).Value;

            var result = _service.Remove(id);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_service.GetNamesForCompany(_companyId), Does.Not.Contain("Temporaire"));
        }

        [Test]
        public void Remove_LeavesTheEmployeeStoredValueUntouched()
        {
            long employeeId = AddEmployee(_companyId, "Recherche");
            long deptId = _service.GetForCompany(_companyId).First(d => d.Name == "Recherche").Id;

            _service.Remove(deptId);

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee employee = uow.Employees.GetById(employeeId);
                Assert.That(employee.Department, Is.EqualTo("Recherche"));
            }
        }
    }
}
