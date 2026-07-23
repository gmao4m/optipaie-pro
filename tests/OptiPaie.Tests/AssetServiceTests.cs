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
    /// Assets module — integration tests against a real SQLite file. They prove the
    /// assign/return lifecycle (one holder at a time, full history), the status guards,
    /// and that holders are resolved through the shared Employees table.
    /// </summary>
    [TestFixture]
    public sealed class AssetServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private IAssetService _service;

        private long _companyId;
        private long _employeeId;
        private long _otherEmployeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-assets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new AssetService(_unitOfWorkFactory);

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(NewEmployee("BENALI", "Karim"));
                _otherEmployeeId = uow.Employees.Insert(NewEmployee("HADDAD", "Sofiane"));
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        private Employee NewEmployee(string last, string first)
        {
            return new Employee
            {
                CompanyId = _companyId,
                LastNameFr = last,
                FirstNameFr = first,
                Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi,
                HireDate = new DateTime(2020, 1, 1),
                BaseSalary = 50000m,
                IsActive = true
            };
        }

        // ---------------------------------------------------------------- creation

        [Test]
        public void Save_NewAsset_IsAvailable()
        {
            long id = _service.Save(NewAsset("Dell Latitude", AssetCategory.Laptop, 120000m)).Value;

            AssetSummary summary = _service.GetSummary(id);
            Assert.That(summary.Status, Is.EqualTo(AssetStatus.Available));
            Assert.That(summary.HolderId, Is.Null);
        }

        [Test]
        public void Save_WithoutName_IsRejected()
        {
            Asset asset = NewAsset(null, AssetCategory.Phone, 20000m);
            Assert.That(_service.Save(asset).IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- assign / return

        [Test]
        public void Assign_MarksTheAssetHeldByTheSharedEmployee()
        {
            long id = _service.Save(NewAsset("iPhone", AssetCategory.Phone, 90000m)).Value;

            Result assigned = _service.Assign(id, _employeeId, new DateTime(2026, 1, 10), "Neuf", null);

            Assert.That(assigned.IsSuccess, Is.True, assigned.Error);
            AssetSummary summary = _service.GetSummary(id);
            Assert.That(summary.Status, Is.EqualTo(AssetStatus.Assigned));
            Assert.That(summary.HolderId, Is.EqualTo(_employeeId));
            Assert.That(summary.HolderName, Is.EqualTo("BENALI Karim"), "the holder name comes from the shared employee");
        }

        [Test]
        public void GetAssignmentHistoryByEmployee_ReturnsOpenAndReturned_WhileHeldReturnsOnlyOpen()
        {
            long held = _service.Save(NewAsset("Laptop", AssetCategory.Laptop, 120000m)).Value;
            _service.Assign(held, _employeeId, new DateTime(2026, 1, 10), "Neuf", null);

            long returned = _service.Save(NewAsset("Phone", AssetCategory.Phone, 60000m)).Value;
            _service.Assign(returned, _employeeId, new DateTime(2026, 1, 5), "Neuf", null);
            _service.Return(returned, new DateTime(2026, 6, 1), "Bon état");

            var history = _service.GetAssignmentHistoryByEmployee(_employeeId);
            Assert.That(history.Count, Is.EqualTo(2), "both the held and the returned asset appear in history");
            Assert.That(history.Any(a => a.AssetName == "Laptop" && a.ReturnedDate == null), Is.True);
            Assert.That(history.Any(a => a.AssetName == "Phone" && a.ReturnedDate != null), Is.True);

            Assert.That(_service.GetHeldByEmployee(_employeeId).Count, Is.EqualTo(1), "held returns only the open one");
        }

        [Test]
        public void Assign_AnAlreadyAssignedAsset_IsRejected()
        {
            long id = _service.Save(NewAsset("Voiture", AssetCategory.Vehicle, 2000000m)).Value;
            _service.Assign(id, _employeeId, new DateTime(2026, 1, 10), null, null);

            Result second = _service.Assign(id, _otherEmployeeId, new DateTime(2026, 1, 11), null, null);

            Assert.That(second.IsFailure, Is.True, "one holder at a time");
        }

        [Test]
        public void Assign_UnknownEmployee_IsRejected()
        {
            long id = _service.Save(NewAsset("Casque", AssetCategory.Uniform, 3000m)).Value;

            Assert.That(_service.Assign(id, 999999, DateTime.Today, null, null).IsFailure, Is.True);
        }

        [Test]
        public void Return_MakesTheAssetAvailableAgain()
        {
            long id = _service.Save(NewAsset("iPhone", AssetCategory.Phone, 90000m)).Value;
            _service.Assign(id, _employeeId, new DateTime(2026, 1, 10), "Neuf", null);

            Result returned = _service.Return(id, new DateTime(2026, 6, 30), "Rayures");

            Assert.That(returned.IsSuccess, Is.True, returned.Error);
            AssetSummary summary = _service.GetSummary(id);
            Assert.That(summary.Status, Is.EqualTo(AssetStatus.Available));
            Assert.That(summary.HolderId, Is.Null);
        }

        [Test]
        public void Return_WhenNotAssigned_IsRejected()
        {
            long id = _service.Save(NewAsset("iPhone", AssetCategory.Phone, 90000m)).Value;

            Assert.That(_service.Return(id, DateTime.Today, null).IsFailure, Is.True);
        }

        [Test]
        public void ReassignAfterReturn_Succeeds_AndKeepsHistory()
        {
            long id = _service.Save(NewAsset("iPhone", AssetCategory.Phone, 90000m)).Value;
            _service.Assign(id, _employeeId, new DateTime(2026, 1, 10), "Neuf", null);
            _service.Return(id, new DateTime(2026, 6, 30), "Bon état");

            Result reassign = _service.Assign(id, _otherEmployeeId, new DateTime(2026, 7, 1), "Bon état", null);

            Assert.That(reassign.IsSuccess, Is.True, reassign.Error);
            IReadOnlyList<AssetAssignmentSummary> history = _service.GetHistory(id);
            Assert.That(history.Count, Is.EqualTo(2), "the previous assignment is preserved");
            Assert.That(history.Count(h => h.IsOpen), Is.EqualTo(1), "exactly one open assignment");
            Assert.That(_service.GetSummary(id).HolderName, Is.EqualTo("HADDAD Sofiane"));
        }

        // ---------------------------------------------------------------- status + delete

        [Test]
        public void SetStatus_UnderRepair_RequiresTheAssetToBeReturnedFirst()
        {
            long id = _service.Save(NewAsset("Voiture", AssetCategory.Vehicle, 2000000m)).Value;
            _service.Assign(id, _employeeId, DateTime.Today, null, null);

            Assert.That(_service.SetStatus(id, AssetStatus.UnderRepair).IsFailure, Is.True);

            _service.Return(id, DateTime.Today, null);
            Assert.That(_service.SetStatus(id, AssetStatus.UnderRepair).IsSuccess, Is.True);
        }

        [Test]
        public void Assign_ARetiredAsset_IsRejected()
        {
            long id = _service.Save(NewAsset("Vieux PC", AssetCategory.Laptop, 10000m)).Value;
            _service.SetStatus(id, AssetStatus.Retired);

            Assert.That(_service.Assign(id, _employeeId, DateTime.Today, null, null).IsFailure, Is.True);
        }

        [Test]
        public void Delete_AnAssignedAsset_IsRejected()
        {
            long id = _service.Save(NewAsset("iPhone", AssetCategory.Phone, 90000m)).Value;
            _service.Assign(id, _employeeId, DateTime.Today, null, null);

            Assert.That(_service.Delete(id).IsFailure, Is.True, "return it before deleting");
        }

        [Test]
        public void Delete_AnAvailableAsset_Succeeds()
        {
            long id = _service.Save(NewAsset("Casque", AssetCategory.Uniform, 3000m)).Value;

            Assert.That(_service.Delete(id).IsSuccess, Is.True);
            Assert.That(_service.Get(id), Is.Null);
        }

        // ---------------------------------------------------------------- queries

        [Test]
        public void GetHeldByEmployee_ListsEverythingTheEmployeeCurrentlyHolds()
        {
            long laptop = _service.Save(NewAsset("Dell", AssetCategory.Laptop, 120000m)).Value;
            long phone = _service.Save(NewAsset("iPhone", AssetCategory.Phone, 90000m)).Value;
            _service.Assign(laptop, _employeeId, DateTime.Today, null, null);
            _service.Assign(phone, _employeeId, DateTime.Today, null, null);

            IReadOnlyList<AssetAssignmentSummary> held = _service.GetHeldByEmployee(_employeeId);

            Assert.That(held.Count, Is.EqualTo(2));
            Assert.That(held.All(h => h.IsOpen), Is.True);
            Assert.That(held.All(h => h.EmployeeName == "BENALI Karim"), Is.True);
        }

        [Test]
        public void GetByCompany_ReturnsAssetsWithTheirHolders()
        {
            long a = _service.Save(NewAsset("Dell", AssetCategory.Laptop, 120000m)).Value;
            _service.Save(NewAsset("iPhone", AssetCategory.Phone, 90000m));
            _service.Assign(a, _employeeId, DateTime.Today, null, null);

            IReadOnlyList<AssetSummary> assets = _service.GetByCompany(_companyId);

            Assert.That(assets.Count, Is.EqualTo(2));
            Assert.That(assets.Count(x => x.HolderId != null), Is.EqualTo(1));
        }

        // -------------------------------------------------- shared vs exclusive (2.2)

        [Test]
        public void Exclusive_BlocksASecondHolder_UntilReturned()
        {
            long id = _service.Save(NewAsset("Laptop", AssetCategory.Laptop, 100000m)).Value; // exclusive by default
            Assert.That(_service.Assign(id, _employeeId, DateTime.Today, "Neuf", null).IsSuccess, Is.True);

            Result blocked = _service.Assign(id, _otherEmployeeId, DateTime.Today, "Neuf", null);
            Assert.That(blocked.IsFailure, Is.True);
            Assert.That(blocked.ErrorCode, Is.EqualTo("Asset_AlreadyAssigned"));
        }

        [Test]
        public void Shared_AllowsSeveralConcurrentHolders_AndReturningOneKeepsTheOthers()
        {
            long id = _service.Save(SharedAsset("Véhicule de pool")).Value;

            Assert.That(_service.Assign(id, _employeeId, DateTime.Today, "RAS", null).IsSuccess, Is.True);
            Assert.That(_service.Assign(id, _otherEmployeeId, DateTime.Today, "RAS", null).IsSuccess, Is.True, "shared allows a 2nd holder");

            // The same employee cannot hold it twice at once.
            Assert.That(_service.Assign(id, _employeeId, DateTime.Today, "RAS", null).ErrorCode, Is.EqualTo("Asset_AlreadyHeldByEmployee"));

            // Both currently hold it.
            Assert.That(_service.GetHeldByEmployee(_employeeId).Any(a => a.AssetId == id), Is.True);
            Assert.That(_service.GetHeldByEmployee(_otherEmployeeId).Any(a => a.AssetId == id), Is.True);

            // Ambiguous return is refused; per-holder return only ends that holder.
            Assert.That(_service.Return(id, DateTime.Today, "RAS").ErrorCode, Is.EqualTo("Asset_MultipleHolders"));

            Assert.That(_service.ReturnFrom(id, _employeeId, DateTime.Today, "RAS").IsSuccess, Is.True);
            Assert.That(_service.GetHeldByEmployee(_employeeId).Any(a => a.AssetId == id), Is.False, "first holder returned");
            Assert.That(_service.GetHeldByEmployee(_otherEmployeeId).Any(a => a.AssetId == id), Is.True, "second holder still holds it");
            Assert.That(_service.GetSummary(id).Status, Is.EqualTo(AssetStatus.Assigned), "still Assigned while a holder remains");

            // Returning the last holder frees the asset.
            Assert.That(_service.ReturnFrom(id, _otherEmployeeId, DateTime.Today, "RAS").IsSuccess, Is.True);
            Assert.That(_service.GetSummary(id).Status, Is.EqualTo(AssetStatus.Available));
        }

        private Asset SharedAsset(string name)
        {
            Asset a = NewAsset(name, AssetCategory.Vehicle, 2000000m);
            a.IsShared = true;
            return a;
        }

        private Asset NewAsset(string name, AssetCategory category, decimal value)
        {
            return new Asset
            {
                CompanyId = _companyId,
                Name = name,
                Category = category,
                PurchaseValue = value,
                PurchaseDate = new DateTime(2025, 1, 1),
                SerialNumber = "SN-" + Guid.NewGuid().ToString("N").Substring(0, 8)
            };
        }
    }
}
