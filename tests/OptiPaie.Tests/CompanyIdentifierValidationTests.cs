using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Company identifiers must not be refused for their LENGTH. A real Algerian NIF varies in
    /// length; forcing a fixed 15-digit count blocked legitimate customers from saving their own
    /// company. These prove any-length NIF/NIS save and reload, NIF stays required (presence only),
    /// and no other identifier is length-checked at entry.
    /// </summary>
    [TestFixture]
    public sealed class CompanyIdentifierValidationTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-nif-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [TestCase("12345", TestName = "NIF shorter than 15 digits")]
        [TestCase("00091602547893100000", TestName = "NIF longer than 15 digits")]
        [TestCase("099816010001234", TestName = "NIF of 15 digits (still fine)")]
        [TestCase("16/00-1234567 B", TestName = "NIF with separators as the customer typed it")]
        public void Nif_OfAnyLength_SavesAndReloadsUnchanged(string nif)
        {
            var res = _companies.Create(new Company { NameFr = "SARL Réelle", Nif = nif, Nis = "42" });
            Assert.That(res.IsSuccess, Is.True, res.Error);

            Company reloaded = _companies.Get(res.Value);
            Assert.That(reloaded.Nif, Is.EqualTo(nif), "le NIF est enregistré et relu tel quel");
            Assert.That(reloaded.Nis, Is.EqualTo("42"), "le NIS de longueur libre est accepté");
        }

        [Test]
        public void Nif_StillRequired_EmptyIsRefused()
        {
            var res = _companies.Create(new Company { NameFr = "SARL Sans NIF", Nif = "  " });
            Assert.That(res.IsFailure, Is.True);
            Assert.That(res.ErrorCode, Is.EqualTo("Company_NifRequired"));
        }

        [Test]
        public void OtherIdentifiers_AreNotLengthChecked_AtEntry()
        {
            // RC, article d'imposition, n° employeur CNAS, RIB: accepted as typed, whatever the length.
            var res = _companies.Create(new Company
            {
                NameFr = "SARL Libre", Nif = "1", Nis = "x", Rc = "16/00-1 B 09",
                ArticleImposition = "42", CnasEmployerNumber = "123", BankAccount = "00799999"
            });
            Assert.That(res.IsSuccess, Is.True, res.Error);
        }
    }
}
