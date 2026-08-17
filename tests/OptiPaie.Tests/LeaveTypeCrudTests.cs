using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;

namespace OptiPaie.Tests
{
    /// <summary>CRUD of configurable leave types: create, edit, deactivate — and a deactivated type is no longer offered at entry.</summary>
    [TestFixture]
    public sealed class LeaveTypeCrudTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uow;
        private LeaveTypeService _types;
        private LeaveService _leave;
        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-ltype-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _types = new LeaveTypeService(_uow);
            _leave = new LeaveService(_uow);

            using (IUnitOfWork uow = _uow.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Types", Nif = "000000000000000" });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        [Test]
        public void Create_Edit_Deactivate_AndDeactivatedNotOfferedAtEntry()
        {
            long id = _types.Save(new LeaveTypeDefinition
            {
                CompanyId = _companyId, Code = "X", LabelAr = "نوع تجريبي", BaseType = LeaveType.Special,
                PaymentCategory = PaymentCategory.EmployerPaid, DecrementsAnnualBalance = false, IsActive = true
            }).Value;

            Assert.That(_leave.GetTypes(_companyId).Any(x => x.Id == id), Is.True, "un type actif est proposé à la saisie");

            // Edit
            LeaveTypeDefinition t = _types.Get(id);
            t.LabelAr = "نوع معدّل";
            t.PaymentCategory = PaymentCategory.SocialSecurity;
            Assert.That(_types.Save(t).IsSuccess, Is.True);
            Assert.That(_types.Get(id).LabelAr, Is.EqualTo("نوع معدّل"));
            Assert.That(_types.Get(id).PaymentCategory, Is.EqualTo(PaymentCategory.SocialSecurity));

            // Deactivate
            Assert.That(_types.SetActive(id, false).IsSuccess, Is.True);
            Assert.That(_leave.GetTypes(_companyId).Any(x => x.Id == id), Is.False, "un type désactivé n'apparaît plus à la saisie");
            Assert.That(_types.GetAll(_companyId).Any(x => x.Id == id), Is.True, "mais reste géré dans l'écran (inactif)");
        }

        [Test]
        public void Save_RequiresArabicLabel()
        {
            var res = _types.Save(new LeaveTypeDefinition { CompanyId = _companyId, LabelAr = "  ", BaseType = LeaveType.Special });
            Assert.That(res.IsFailure, Is.True);
            Assert.That(res.ErrorCode, Is.EqualTo("LeaveType_LabelRequired"));
        }

        [Test]
        public void GlobalDefaults_AreSeededAndVisible()
        {
            // Migration 0031 seeds the standard catalogue globally.
            var all = _types.GetAll(_companyId);
            Assert.That(all.Any(t => t.Code == "ANNUAL"), Is.True);
            Assert.That(all.Any(t => t.Code == "PILGRIMAGE" && t.OncePerCareer), Is.True, "pèlerinage : une fois dans la carrière");
            Assert.That(all.Count(t => t.BaseType == LeaveType.Special), Is.GreaterThanOrEqualTo(6), "6 événements familiaux + pèlerinage");
        }
    }
}
