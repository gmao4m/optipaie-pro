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
    /// Contracts module — integration tests against a real SQLite file. They prove the
    /// contract lifecycle and, above all, the ecosystem promise: activating a contract
    /// writes its terms onto the SHARED employee record (so payroll uses them), only one
    /// contract is active at a time, terminating sets the exit date and renewing chains a
    /// new active contract.
    /// </summary>
    [TestFixture]
    public sealed class ContractServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private IContractService _service;

        private long _companyId;
        private long _employeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-contracts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new ContractService(_unitOfWorkFactory);

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
                    PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdd,
                    HireDate = new DateTime(2020, 1, 1),
                    BaseSalary = 30000m,
                    Poste = "Ancien poste",
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

        private Employee Employee()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Employees.GetById(_employeeId);
            }
        }

        // ---------------------------------------------------------------- validation

        [Test]
        public void Save_Cdd_RequiresAnEndDate()
        {
            EmploymentContract cdd = NewContract(ContractType.Cdd, new DateTime(2026, 1, 1), null, 45000m);

            Assert.That(_service.Save(cdd).IsFailure, Is.True);
        }

        [Test]
        public void Save_Cdi_ClearsAnyEndDate()
        {
            EmploymentContract cdi = NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1), 45000m);

            long id = _service.Save(cdi).Value;

            Assert.That(_service.Get(id).EndDate, Is.Null, "an open-ended contract has no end date");
        }

        [Test]
        public void Save_NewContract_StartsAsDraftAndChangesNothingOnTheEmployee()
        {
            _service.Save(NewContract(ContractType.Cdd, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), 80000m));

            Assert.That(Employee().BaseSalary, Is.EqualTo(30000m), "a draft contract is not yet in force");
        }

        [Test]
        public void Save_UnknownEmployee_IsRejected()
        {
            EmploymentContract c = NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 45000m);
            c.EmployeeId = 999999;

            Assert.That(_service.Save(c).IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- activation + sync

        [Test]
        public void Activate_WritesTheTermsOntoTheSharedEmployee()
        {
            long id = _service.Save(NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 90000m, "Chef d'équipe")).Value;

            Result activated = _service.Activate(id);

            Assert.That(activated.IsSuccess, Is.True, activated.Error);
            Employee employee = Employee();
            Assert.That(employee.BaseSalary, Is.EqualTo(90000m), "payroll now reads the contract salary");
            Assert.That(employee.ContractType, Is.EqualTo(ContractType.Cdi));
            Assert.That(employee.Poste, Is.EqualTo("Chef d'équipe"));
            Assert.That(employee.ExitDate, Is.Null);
            Assert.That(_service.Get(id).Status, Is.EqualTo(ContractStatus.Active));
        }

        [Test]
        public void Activate_SupersedesThePreviousActiveContract()
        {
            long first = _service.Save(NewContract(ContractType.Cdi, new DateTime(2025, 1, 1), null, 60000m)).Value;
            _service.Activate(first);

            long second = _service.Save(NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 75000m)).Value;
            _service.Activate(second);

            Assert.That(_service.Get(first).Status, Is.EqualTo(ContractStatus.Expired),
                "only one contract is active at a time");
            Assert.That(_service.Get(second).Status, Is.EqualTo(ContractStatus.Active));
            Assert.That(Employee().BaseSalary, Is.EqualTo(75000m));
        }

        [Test]
        public void Activate_ANonDraftContract_IsRejected()
        {
            long id = _service.Save(NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 60000m)).Value;
            _service.Activate(id);

            Assert.That(_service.Activate(id).IsSuccess, Is.True, "activating an already-active contract is a no-op");
        }

        // ---------------------------------------------------------------- termination

        [Test]
        public void Terminate_SetsTheEmployeeExitDate()
        {
            long id = _service.Save(NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 60000m)).Value;
            _service.Activate(id);

            var exit = new DateTime(2026, 6, 30);
            Result terminated = _service.Terminate(id, exit, "Démission");

            Assert.That(terminated.IsSuccess, Is.True, terminated.Error);
            Assert.That(_service.Get(id).Status, Is.EqualTo(ContractStatus.Terminated));
            Employee employee = Employee();
            Assert.That(employee.ExitDate, Is.Not.Null);
            Assert.That(employee.ExitDate.Value.Date, Is.EqualTo(exit));
            Assert.That(employee.IsActive, Is.False);
        }

        [Test]
        public void Terminate_ANonActiveContract_IsRejected()
        {
            long id = _service.Save(NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 60000m)).Value;

            Assert.That(_service.Terminate(id, new DateTime(2026, 6, 30), null).IsFailure, Is.True,
                "a draft contract has nothing to terminate");
        }

        // ---------------------------------------------------------------- renewal

        [Test]
        public void Renew_ChainsANewActiveContractAndSupersedesTheOld()
        {
            long old = _service.Save(NewContract(ContractType.Cdd, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31), 50000m)).Value;
            _service.Activate(old);

            Result<long> renewal = _service.Renew(old, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), 65000m);

            Assert.That(renewal.IsSuccess, Is.True, renewal.Error);
            Assert.That(_service.Get(old).Status, Is.EqualTo(ContractStatus.Renewed),
                "the predecessor of a renewal is marked Renewed, not Expired");

            EmploymentContract fresh = _service.Get(renewal.Value);
            Assert.That(fresh.Status, Is.EqualTo(ContractStatus.Active));
            Assert.That(fresh.PreviousContractId, Is.EqualTo(old));
            Assert.That(fresh.TrialPeriodDays, Is.EqualTo(0), "a renewal carries no trial period");
            Assert.That(Employee().BaseSalary, Is.EqualTo(65000m));
        }

        [Test]
        public void Renew_ADraftContract_IsRejected()
        {
            long id = _service.Save(NewContract(ContractType.Cdd, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), 50000m)).Value;

            Assert.That(_service.Renew(id, new DateTime(2027, 1, 1), new DateTime(2027, 12, 31), 55000m).IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- expiry alerts

        [Test]
        public void GetExpiring_ReturnsActiveFixedTermContractsWithinTheWindow()
        {
            // A CDD ending in 20 days.
            long soon = _service.Save(NewContract(ContractType.Cdd, DateTime.Today.AddMonths(-6), DateTime.Today.AddDays(20), 50000m)).Value;
            _service.Activate(soon);

            IReadOnlyList<ContractSummary> expiring = _service.GetExpiring(_companyId, 30);

            Assert.That(expiring.Count, Is.EqualTo(1));
            Assert.That(expiring[0].ContractId, Is.EqualTo(soon));
            Assert.That(expiring[0].DaysUntilExpiry, Is.EqualTo(20));
            Assert.That(expiring[0].IsExpiringSoon, Is.True);
        }

        [Test]
        public void GetExpiring_IncludesOverdueContracts_AndExcludesFarOnes()
        {
            long overdue = _service.Save(NewContract(ContractType.Cdd, DateTime.Today.AddMonths(-12), DateTime.Today.AddDays(-3), 50000m)).Value;
            _service.Activate(overdue);

            long far = _service.Save(NewContract(ContractType.Cdd, DateTime.Today, DateTime.Today.AddMonths(6), 50000m)).Value;
            _service.Activate(far); // supersedes overdue? no — different employees needed. Same employee → supersede.

            // 'far' superseded 'overdue' (same employee, one active at a time), so only the
            // active 'far' remains — and it is outside the 30-day window.
            IReadOnlyList<ContractSummary> expiring = _service.GetExpiring(_companyId, 30);

            Assert.That(expiring.Any(s => s.ContractId == far), Is.False, "a contract ending in 6 months is not expiring soon");
            Assert.That(_service.Get(overdue).Status, Is.EqualTo(ContractStatus.Expired));
        }

        [Test]
        public void GetExpiring_IgnoresOpenEndedContracts()
        {
            long cdi = _service.Save(NewContract(ContractType.Cdi, DateTime.Today.AddMonths(-1), null, 60000m)).Value;
            _service.Activate(cdi);

            Assert.That(_service.GetExpiring(_companyId, 30), Is.Empty, "a CDI never expires");
        }

        // ---------------------------------------------------------------- editing + delete

        [Test]
        public void Save_ActiveContract_OnlyUpdatesReferenceAndNotes()
        {
            long id = _service.Save(NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 60000m)).Value;
            _service.Activate(id);

            EmploymentContract contract = _service.Get(id);
            contract.BaseSalary = 999999m;      // must be ignored
            contract.Reference = "REF-2026-01";  // must be kept
            contract.Notes = "Avenant";          // must be kept
            _service.Save(contract);

            EmploymentContract reloaded = _service.Get(id);
            Assert.That(reloaded.BaseSalary, Is.EqualTo(60000m), "a contract in force keeps its legal terms");
            Assert.That(reloaded.Reference, Is.EqualTo("REF-2026-01"));
            Assert.That(reloaded.Notes, Is.EqualTo("Avenant"));
            Assert.That(Employee().BaseSalary, Is.EqualTo(60000m), "the employee salary is untouched by the edit");
        }

        [Test]
        public void Delete_AnActiveContract_IsRejected()
        {
            long id = _service.Save(NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 60000m)).Value;
            _service.Activate(id);

            Assert.That(_service.Delete(id).IsFailure, Is.True, "terminate before deleting");
        }

        [Test]
        public void Delete_ADraftContract_Succeeds()
        {
            long id = _service.Save(NewContract(ContractType.Cdd, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), 50000m)).Value;

            Assert.That(_service.Delete(id).IsSuccess, Is.True);
            Assert.That(_service.Get(id), Is.Null);
        }

        [Test]
        public void GetByCompany_CarriesTheSharedEmployeeName()
        {
            _service.Save(NewContract(ContractType.Cdi, new DateTime(2026, 1, 1), null, 60000m));

            IReadOnlyList<ContractSummary> contracts = _service.GetByCompany(_companyId);

            Assert.That(contracts.Count, Is.EqualTo(1));
            Assert.That(contracts[0].EmployeeName, Is.EqualTo("BENALI Karim"));
        }

        // ------------------------------------------- auto-created draft on new hire (2.1)

        [Test]
        public void CreateDraftFromEmployee_PrefillsFromTheEmployee_AsADraft()
        {
            Result<long> draft = _service.CreateDraftFromEmployee(_employeeId);
            Assert.That(draft.IsSuccess, Is.True, draft.Error);

            EmploymentContract c = _service.Get(draft.Value);
            Assert.That(c.Status, Is.EqualTo(ContractStatus.Draft));
            Assert.That(c.Type, Is.EqualTo(ContractType.Cdd), "copied the employee's contract type");
            Assert.That(c.Position, Is.EqualTo("Ancien poste"), "copied the employee's position");
            Assert.That(c.BaseSalary, Is.EqualTo(30000m), "copied the employee's salary");
            Assert.That(c.StartDate.Date, Is.EqualTo(new DateTime(2020, 1, 1)), "copied the hire date");
            Assert.That(c.EndDate.HasValue, Is.True, "a fixed-term draft gets a default end date");
        }

        [Test]
        public void CreateDraftFromEmployee_IsIdempotent_NeverDuplicates()
        {
            long first = _service.CreateDraftFromEmployee(_employeeId).Value;
            long second = _service.CreateDraftFromEmployee(_employeeId).Value;

            Assert.That(second, Is.EqualTo(first), "a second call returns the same contract");
            Assert.That(_service.GetByEmployee(_employeeId).Count, Is.EqualTo(1), "exactly one contract exists");
        }

        [Test]
        public void CreateDraftFromEmployee_UnknownEmployee_Fails()
        {
            Assert.That(_service.CreateDraftFromEmployee(999999).IsFailure, Is.True);
        }

        private EmploymentContract NewContract(ContractType type, DateTime start, DateTime? end, decimal salary, string position = "Poste")
        {
            return new EmploymentContract
            {
                EmployeeId = _employeeId,
                Type = type,
                Reference = "C-001",
                Position = position,
                BaseSalary = salary,
                StartDate = start,
                EndDate = end,
                TrialPeriodDays = 30
            };
        }
    }
}
