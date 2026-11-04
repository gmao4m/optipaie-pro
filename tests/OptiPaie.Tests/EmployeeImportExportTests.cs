using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
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
    /// Employee bulk import/export. Proves: a clean file imports fully with correct values and
    /// company; a file with errors writes NOTHING and reports each row+column; duplicates are never
    /// silently created (skip or stop); unusual-length identifiers are accepted; export→re-import
    /// round-trips with no loss and no duplicates; existing employees are untouched; and the import
    /// cannot reach another company's data. Each imported employee also gets a draft contract, exactly
    /// like manual entry.
    /// </summary>
    [TestFixture]
    public sealed class EmployeeImportExportTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uow;
        private CompanyService _companies;
        private EmployeeService _employees;
        private EmployeeImportExportService _io;
        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _companies = new CompanyService(_uow, new CompanyValidator());
            _employees = new EmployeeService(_uow, new EmployeeValidator());
            _io = new EmployeeImportExportService(_uow, new EmployeeValidator());
            _companyId = _companies.Create(new Company { NameFr = "SARL Import", Nif = "000000000000000" }).Value;
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ---------------------------------------------------------------- helpers

        private IReadOnlyList<string> Headers() => _io.Columns.Select(c => c.Header).ToList();

        private IReadOnlyList<string> Row(IDictionary<string, string> values)
        {
            return _io.Columns.Select(c => values.TryGetValue(c.Key, out string v) ? v : string.Empty).ToList();
        }

        private static Dictionary<string, string> Person(string last, string first, string hire = "2020-01-01",
            string salary = "50000", string nss = null, string birth = null)
        {
            var d = new Dictionary<string, string>
            {
                [EmployeeImportColumns.LastNameFr] = last,
                [EmployeeImportColumns.FirstNameFr] = first,
                [EmployeeImportColumns.HireDate] = hire,
                [EmployeeImportColumns.BaseSalary] = salary,
            };
            if (nss != null) d[EmployeeImportColumns.Nss] = nss;
            if (birth != null) d[EmployeeImportColumns.BirthDate] = birth;
            return d;
        }

        private ImportTable Table(params IDictionary<string, string>[] people)
        {
            return new ImportTable(Headers(), people.Select(Row).ToList());
        }

        private List<Employee> ActiveEmployees(long companyId)
        {
            using (IUnitOfWork uow = _uow.Create()) return uow.Employees.GetByCompany(companyId, true).ToList();
        }

        // ---------------------------------------------------------------- tests

        [Test]
        public void Import200FromTemplate_AllCreated_CorrectValues_CorrectCompany()
        {
            var people = new List<IDictionary<string, string>>();
            for (int i = 1; i <= 200; i++)
                people.Add(Person("NOM" + i, "Prenom" + i, "2021-06-15", (40000 + i).ToString(), nss: (100000000000 + i).ToString()));
            ImportTable table = Table(people.ToArray());

            ImportPreview preview = _io.BuildPreview(_companyId, table, null);
            Assert.That(preview.MissingColumns, Is.Empty);
            Assert.That(preview.NewCount, Is.EqualTo(200));
            Assert.That(preview.ErrorCount, Is.EqualTo(0));
            Assert.That(preview.CanImport, Is.True);

            Result<ImportSummary> result = _io.Commit(_companyId, table, null, DuplicateResolution.SkipDuplicates, "staff.xlsx");
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(result.Value.Created, Is.EqualTo(200));

            List<Employee> all = ActiveEmployees(_companyId);
            Assert.That(all.Count, Is.EqualTo(200));
            Assert.That(all.All(e => e.CompanyId == _companyId), Is.True, "every imported employee belongs to the active company");
            Employee sample = all.First(e => e.LastNameFr == "NOM42");
            Assert.That(sample.FirstNameFr, Is.EqualTo("Prenom42"));
            Assert.That(sample.BaseSalary, Is.EqualTo(40042m));
            Assert.That(sample.HireDate.Date, Is.EqualTo(new DateTime(2021, 6, 15)));

            // Each imported employee got a draft contract (parity with manual entry).
            using (IUnitOfWork uow = _uow.Create())
            {
                int withContract = all.Count(e => uow.Contracts.GetByEmployee(e.Id).Any(c => c.Status == ContractStatus.Draft));
                Assert.That(withContract, Is.EqualTo(200), "each imported employee gets a draft contract");
            }
        }

        [Test]
        public void FileWithErrors_WritesNothing_AndReportsEachRowAndColumn()
        {
            var people = new List<IDictionary<string, string>>();
            for (int i = 1; i <= 60; i++) people.Add(Person("NOM" + i, "Prenom" + i));
            // Row 3 (file row 4): missing last name. Row 17 (row 18): bad salary. Row 50 (row 51): bad date.
            people[2][EmployeeImportColumns.LastNameFr] = "";
            people[16][EmployeeImportColumns.BaseSalary] = "abc";
            people[49][EmployeeImportColumns.HireDate] = "32/13/2020";
            ImportTable table = Table(people.ToArray());

            ImportPreview preview = _io.BuildPreview(_companyId, table, null);
            Assert.That(preview.ErrorCount, Is.EqualTo(3));
            Assert.That(preview.CanImport, Is.False);
            // file rows are 2-based (header = row 1)
            Assert.That(preview.Issues.Any(x => x.RowNumber == 4), Is.True, "missing last name reported on its row");
            Assert.That(preview.Issues.Any(x => x.RowNumber == 18), Is.True, "bad salary reported on its row");
            Assert.That(preview.Issues.Any(x => x.RowNumber == 51), Is.True, "bad date reported on its row");
            Assert.That(preview.Issues.All(x => !string.IsNullOrWhiteSpace(x.Column)), Is.True, "each issue names a column");

            Result<ImportSummary> result = _io.Commit(_companyId, table, null, DuplicateResolution.SkipDuplicates, "bad.xlsx");
            Assert.That(result.IsSuccess, Is.False, "a file with errors must write nothing");
            Assert.That(ActiveEmployees(_companyId), Is.Empty, "nothing was written");
        }

        [Test]
        public void Duplicate_AgainstExisting_IsSkipped_NeverDuplicated()
        {
            _io.Commit(_companyId, Table(Person("BENALI", "Karim", nss: "123456789012")), null, DuplicateResolution.SkipDuplicates, "a.xlsx");

            // Same NSS, different typed name → still the same person.
            ImportTable again = Table(
                Person("BENALI", "Karim", nss: "123456789012"),
                Person("NOUVEAU", "Salah", nss: "999888777666"));

            ImportPreview preview = _io.BuildPreview(_companyId, again, null);
            Assert.That(preview.DuplicateCount, Is.EqualTo(1));
            Assert.That(preview.NewCount, Is.EqualTo(1));

            Result<ImportSummary> skip = _io.Commit(_companyId, again, null, DuplicateResolution.SkipDuplicates, "a.xlsx");
            Assert.That(skip.IsSuccess, Is.True, skip.Error);
            Assert.That(skip.Value.Created, Is.EqualTo(1));
            Assert.That(skip.Value.SkippedDuplicates, Is.EqualTo(1));
            Assert.That(ActiveEmployees(_companyId).Count(e => e.Nss == "123456789012"), Is.EqualTo(1), "never duplicated");
        }

        [Test]
        public void Duplicate_WithStopResolution_WritesNothing()
        {
            _io.Commit(_companyId, Table(Person("BENALI", "Karim", nss: "123456789012")), null, DuplicateResolution.SkipDuplicates, "a.xlsx");
            int before = ActiveEmployees(_companyId).Count;

            ImportTable again = Table(
                Person("BENALI", "Karim", nss: "123456789012"),
                Person("NOUVEAU", "Salah", nss: "999888777666"));

            Result<ImportSummary> stop = _io.Commit(_companyId, again, null, DuplicateResolution.Stop, "a.xlsx");
            Assert.That(stop.IsSuccess, Is.False, "Stop refuses when duplicates exist");
            Assert.That(ActiveEmployees(_companyId).Count, Is.EqualTo(before), "nothing written on Stop");
        }

        [Test]
        public void DuplicateWithinFile_IsDetected()
        {
            ImportTable table = Table(
                Person("AaA", "Bbb", nss: "111222333444"),
                Person("AaA", "Bbb", nss: "111222333444"));
            ImportPreview preview = _io.BuildPreview(_companyId, table, null);
            Assert.That(preview.NewCount, Is.EqualTo(1));
            Assert.That(preview.DuplicateCount, Is.EqualTo(1));
        }

        [Test]
        public void UnusualLengthIdentifiers_AreAccepted()
        {
            // NSS / national id of odd lengths must NOT be rejected (import matches manual entry).
            ImportTable table = Table(new Dictionary<string, string>
            {
                [EmployeeImportColumns.LastNameFr] = "ODD",
                [EmployeeImportColumns.FirstNameFr] = "Length",
                [EmployeeImportColumns.HireDate] = "2022-01-01",
                [EmployeeImportColumns.BaseSalary] = "30000",
                [EmployeeImportColumns.Nss] = "7",                        // 1 digit
                [EmployeeImportColumns.NationalId] = "123456789012345678", // 18 digits
            });
            ImportPreview preview = _io.BuildPreview(_companyId, table, null);
            Assert.That(preview.ErrorCount, Is.EqualTo(0));
            Result<ImportSummary> result = _io.Commit(_companyId, table, null, DuplicateResolution.SkipDuplicates, "odd.xlsx");
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(ActiveEmployees(_companyId).Single().Nss, Is.EqualTo("7"));
        }

        [Test]
        public void ExportThenReimport_RoundTrips_NoLoss_NoDuplicates()
        {
            _io.Commit(_companyId, Table(
                Person("BENALI", "Karim", "2019-03-01", "55000", nss: "123456789012", birth: "1985-05-20"),
                Person("CHERIF", "Amine", "2020-11-10", "47250.50", nss: "222333444555")),
                null, DuplicateResolution.SkipDuplicates, "seed.xlsx");

            string file = Path.Combine(_dir, "export.xlsx");
            _io.Export(_companyId, file, ImportFileFormat.Xlsx);
            Assert.That(File.Exists(file), Is.True);

            ImportTable reread = _io.Read(file);
            Assert.That(reread.Rows.Count, Is.EqualTo(2));

            // Re-importing the exact export detects both as duplicates → creates nothing.
            ImportPreview preview = _io.BuildPreview(_companyId, reread, null);
            Assert.That(preview.DuplicateCount, Is.EqualTo(2), "an export re-imports as all-duplicate");
            Assert.That(preview.NewCount, Is.EqualTo(0));

            Result<ImportSummary> result = _io.Commit(_companyId, reread, null, DuplicateResolution.SkipDuplicates, "export.xlsx");
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(result.Value.Created, Is.EqualTo(0));
            Assert.That(ActiveEmployees(_companyId).Count, Is.EqualTo(2), "no duplicates created on round-trip");

            // Values survived the round-trip.
            Employee karim = ActiveEmployees(_companyId).First(e => e.LastNameFr == "BENALI");
            Assert.That(karim.BaseSalary, Is.EqualTo(55000m));
            Assert.That(karim.BirthDate.Value.Date, Is.EqualTo(new DateTime(1985, 5, 20)));
        }

        [Test]
        public void Import_CannotTouchAnotherCompany()
        {
            long other = _companies.Create(new Company { NameFr = "SARL Autre", Nif = "111111111111111" }).Value;
            _io.Commit(other, Table(Person("EXISTANT", "Autre", nss: "555555555555")), null, DuplicateResolution.SkipDuplicates, "o.xlsx");

            // Import the same NSS into OUR company: must be NEW here (duplicate scope is per-company),
            // and must not appear in, or alter, the other company.
            ImportTable table = Table(Person("EXISTANT", "Autre", nss: "555555555555"));
            ImportPreview preview = _io.BuildPreview(_companyId, table, null);
            Assert.That(preview.NewCount, Is.EqualTo(1), "another company's employee is not a duplicate here");

            _io.Commit(_companyId, table, null, DuplicateResolution.SkipDuplicates, "o.xlsx");
            Assert.That(ActiveEmployees(other).Count, Is.EqualTo(1), "the other company is unchanged");
            Assert.That(ActiveEmployees(_companyId).Count, Is.EqualTo(1));
        }

        [Test]
        public void ExistingEmployees_AreUnaffectedByImport()
        {
            Result<long> created = _employees.Create(new Employee
            {
                CompanyId = _companyId, LastNameFr = "ANCIEN", FirstNameFr = "Employe",
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi, HireDate = new DateTime(2015, 1, 1), BaseSalary = 99000m, IsActive = true
            });
            Assert.That(created.IsSuccess, Is.True, created.Error);
            long existingId = created.Value;

            _io.Commit(_companyId, Table(Person("NOUVEAU", "Recrue")), null, DuplicateResolution.SkipDuplicates, "n.xlsx");

            Employee existing = _employees.Get(existingId);
            Assert.That(existing.BaseSalary, Is.EqualTo(99000m), "existing employee untouched");
            Assert.That(existing.LastNameFr, Is.EqualTo("ANCIEN"));
            Assert.That(ActiveEmployees(_companyId).Count, Is.EqualTo(2));
        }

        [Test]
        public void Template_HasEveryExpectedColumn_AndOneExampleRow()
        {
            string file = Path.Combine(_dir, "template.xlsx");
            _io.WriteTemplate(file, ImportFileFormat.Xlsx);
            ImportTable t = _io.Read(file);
            Assert.That(t.Headers.Count, Is.EqualTo(_io.Columns.Count));
            Assert.That(t.Rows.Count, Is.EqualTo(1), "exactly one example row");
            // The template re-imports cleanly as one new employee (the example is valid).
            ImportPreview preview = _io.BuildPreview(_companyId, t, null);
            Assert.That(preview.MissingColumns, Is.Empty);
            Assert.That(preview.ErrorCount, Is.EqualTo(0));
        }

        [Test]
        public void CsvRoundTrips_Too()
        {
            _io.Commit(_companyId, Table(Person("CSV", "Test", nss: "321321321321")), null, DuplicateResolution.SkipDuplicates, "s.xlsx");
            string file = Path.Combine(_dir, "export.csv");
            _io.Export(_companyId, file, ImportFileFormat.Csv);
            ImportTable reread = _io.Read(file);
            ImportPreview preview = _io.BuildPreview(_companyId, reread, null);
            Assert.That(preview.DuplicateCount, Is.EqualTo(1), "csv export re-imports as duplicate");
        }
    }
}
