using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.ImportExport;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Column mapping: a customer's own file (its own headers, French or Arabic) is NOT refused — the
    /// caller supplies a field→header mapping and the import validates/commits exactly as for the
    /// template. A required field left unmapped is blocked and named. The per-company mapping is a
    /// plain settings string, round-tripped here.
    /// </summary>
    [TestFixture]
    public sealed class EmployeeImportMappingTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private SettingsService _settings;
        private EmployeeImportExportService _io;
        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-map-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _settings = new SettingsService(_uow);
            _io = new EmployeeImportExportService(_uow, new EmployeeValidator());
            _companyId = _companies.Create(new Company { NameFr = "SARL Map", Nif = "000000000000000" }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private List<Employee> Active() { using (IUnitOfWork u = _uow.Create()) return u.Employees.GetByCompany(_companyId, true).ToList(); }

        [Test]
        public void CustomFrenchHeaders_MapAndImport()
        {
            var table = new ImportTable(
                new[] { "Nom de famille", "Le prénom", "Date d'embauche", "Rémunération", "Sécurité sociale" },
                new List<IReadOnlyList<string>>
                {
                    new[] { "BENALI", "Karim", "2020-01-01", "50000", "123456789012" },
                    new[] { "CHERIF", "Amine", "2021-05-10", "47000", "222333444555" },
                });
            var mapping = new Dictionary<string, string>
            {
                [EmployeeImportColumns.LastNameFr] = "Nom de famille",
                [EmployeeImportColumns.FirstNameFr] = "Le prénom",
                [EmployeeImportColumns.HireDate] = "Date d'embauche",
                [EmployeeImportColumns.BaseSalary] = "Rémunération",
                [EmployeeImportColumns.Nss] = "Sécurité sociale",
            };

            ImportPreview preview = _io.BuildPreview(_companyId, table, mapping);
            Assert.That(preview.MissingColumns, Is.Empty);
            Assert.That(preview.NewCount, Is.EqualTo(2));
            Assert.That(preview.ErrorCount, Is.EqualTo(0));

            Result<ImportSummary> result = _io.Commit(_companyId, table, mapping, DuplicateResolution.SkipDuplicates, "client.xlsx");
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(Active().Count, Is.EqualTo(2));
            Assert.That(Active().Single(e => e.LastNameFr == "BENALI").BaseSalary, Is.EqualTo(50000m));
        }

        [Test]
        public void ArabicHeaders_MapAndImport()
        {
            var table = new ImportTable(
                new[] { "اللقب", "الاسم", "تاريخ التوظيف", "الأجر", "رقم الضمان" },
                new List<IReadOnlyList<string>>
                {
                    new[] { "بن علي", "كريم", "2020-01-01", "50000", "123456789012" },
                });
            var mapping = new Dictionary<string, string>
            {
                [EmployeeImportColumns.LastNameFr] = "اللقب",     // the user mapped their Arabic column to our (French-keyed) name field
                [EmployeeImportColumns.FirstNameFr] = "الاسم",
                [EmployeeImportColumns.HireDate] = "تاريخ التوظيف",
                [EmployeeImportColumns.BaseSalary] = "الأجر",
                [EmployeeImportColumns.Nss] = "رقم الضمان",
            };

            ImportPreview preview = _io.BuildPreview(_companyId, table, mapping);
            Assert.That(preview.MissingColumns, Is.Empty);
            Assert.That(preview.NewCount, Is.EqualTo(1));

            Result<ImportSummary> result = _io.Commit(_companyId, table, mapping, DuplicateResolution.SkipDuplicates, "ملف.xlsx");
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(Active().Single().LastNameFr, Is.EqualTo("بن علي"));
        }

        [Test]
        public void MissingRequiredColumn_IsBlocked_AndNamed()
        {
            // No column maps to (or auto-matches) the required Base salary.
            var table = new ImportTable(
                new[] { "Nom", "Prénom", "Embauche" },
                new List<IReadOnlyList<string>> { new[] { "X", "Y", "2020-01-01" } });
            var mapping = new Dictionary<string, string>
            {
                [EmployeeImportColumns.LastNameFr] = "Nom",
                [EmployeeImportColumns.FirstNameFr] = "Prénom",
                [EmployeeImportColumns.HireDate] = "Embauche",
                // BaseSalary deliberately unmapped
            };

            ImportPreview preview = _io.BuildPreview(_companyId, table, mapping);
            Assert.That(preview.MissingColumns, Is.Not.Empty);
            Assert.That(preview.MissingColumns.Any(m => m.Contains("Salaire de base")), Is.True, "the missing required field is named");

            Result<ImportSummary> result = _io.Commit(_companyId, table, mapping, DuplicateResolution.SkipDuplicates, "x.xlsx");
            Assert.That(result.IsSuccess, Is.False, "a missing required column blocks the import");
            Assert.That(Active(), Is.Empty, "nothing written");
        }

        [Test]
        public void PerCompanyMapping_PersistsAndRoundTrips()
        {
            // The VM remembers the mapping under this exact key; prove the settings substrate round-trips it.
            string key = "EmployeeImport.Mapping." + _companyId;
            string serialized = EmployeeImportColumns.LastNameFr + "=Nom de famille\n" +
                                EmployeeImportColumns.BaseSalary + "=Rémunération";
            _settings.Set(key, serialized);

            string read = _settings.Get(key, string.Empty);
            var parsed = read.Split('\n').Where(l => l.Contains("="))
                .ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));
            Assert.That(parsed[EmployeeImportColumns.LastNameFr], Is.EqualTo("Nom de famille"));
            Assert.That(parsed[EmployeeImportColumns.BaseSalary], Is.EqualTo("Rémunération"));
        }
    }
}
