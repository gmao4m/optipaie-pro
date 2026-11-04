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
using ClosedXML.Excel;

namespace OptiPaie.Tests
{
    /// <summary>
    /// END-TO-END verification of employee import/export, producing the REAL files a user opens.
    /// [Explicit] — run on demand. Seeds a demo company, writes the blank template, exports the
    /// company, edits the exported file (adds one employee), re-imports it, and confirms the existing
    /// rows round-trip as duplicates (0 re-created) while the edit creates exactly one. Prints the
    /// template and export paths.
    /// </summary>
    [TestFixture, Explicit]
    public sealed class EmployeeImportExportVerificationHarness
    {
        [Test]
        public void Export_Edit_Reimport_Demo()
        {
            string db = Path.Combine(Path.GetTempPath(), "optipaie-verify-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(db);
            string docs = Path.Combine(RepoRoot(), "docs", "verification-employee-import");
            Directory.CreateDirectory(docs);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(db, "demo.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            IUnitOfWorkFactory uow = new UnitOfWorkFactory(factory);

            var companies = new CompanyService(uow, new CompanyValidator());
            var employees = new EmployeeService(uow, new EmployeeValidator());
            var io = new EmployeeImportExportService(uow, new EmployeeValidator());

            long companyId = companies.Create(new Company { NameFr = "SARL Démo OptiPaie", Nif = "000000000000000" }).Value;
            Seed(employees, companyId, "BENALI", "Karim", "1985-05-20", "55000", "123456789012");
            Seed(employees, companyId, "CHERIF", "Amine", "1990-11-02", "47000", "222333444555");
            Seed(employees, companyId, "HADDAD", "Sara", "1992-07-14", "62000", "333444555666");

            // 1) blank template
            string template = Path.Combine(docs, "modele_employes.xlsx");
            io.WriteTemplate(template, ImportFileFormat.Xlsx);

            // 2) export the demo company
            string export = Path.Combine(docs, "employes_demo.xlsx");
            io.Export(companyId, export, ImportFileFormat.Xlsx);

            // 3) edit the exported file: append one brand-new employee, then re-import
            ImportTable edited = AppendPerson(io.Read(export), io, "ZIANI", "Walid", "1988-02-29", "70000", "444555666777");
            ImportPreview preview = io.BuildPreview(companyId, edited, null);
            Result<ImportSummary> result = io.Commit(companyId, edited, null, DuplicateResolution.SkipDuplicates, "employes_demo (modifié).xlsx");

            // assertions
            Assert.That(preview.DuplicateCount, Is.EqualTo(3), "the 3 exported employees re-import as duplicates");
            Assert.That(preview.NewCount, Is.EqualTo(1), "the edited-in employee is the only new one");
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(result.Value.Created, Is.EqualTo(1));
            Assert.That(result.Value.SkippedDuplicates, Is.EqualTo(3));
            int total = employees.GetByCompany(companyId, true).Count;
            Assert.That(total, Is.EqualTo(4), "3 original + 1 imported, no duplicates");

            TestContext.Out.WriteLine("Template  : " + Path.GetFullPath(template));
            TestContext.Out.WriteLine("Export    : " + Path.GetFullPath(export));
            TestContext.Out.WriteLine("Employees after edit+reimport: " + total + " (expected 4)");
        }

        [Test]
        public void WeirdHeaders_Map_And_Import()
        {
            string db = Path.Combine(Path.GetTempPath(), "optipaie-verify-map-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(db);
            string docs = Path.Combine(RepoRoot(), "docs", "verification-employee-import");
            Directory.CreateDirectory(docs);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(db, "demo.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            IUnitOfWorkFactory uow = new UnitOfWorkFactory(factory);
            var companies = new CompanyService(uow, new CompanyValidator());
            var employees = new EmployeeService(uow, new EmployeeValidator());
            var io = new EmployeeImportExportService(uow, new EmployeeValidator());
            long companyId = companies.Create(new Company { NameFr = "SARL Démo", Nif = "000000000000000" }).Value;

            // A real customer file whose headers match NOTHING in our template.
            string file = Path.Combine(docs, "fichier_client_entetes_personnalisees.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Personnel");
                string[] headers = { "MATRICULE", "NOM & PRENOM(séparés)", "Nom", "Prénom", "Entrée société", "Paie mensuelle", "N° CNAS" };
                for (int i = 0; i < headers.Length; i++) { ws.Cell(1, i + 1).Value = headers[i]; ws.Cell(1, i + 1).Style.Font.Bold = true; }
                object[,] data =
                {
                    { "M001", "", "DJELLOUL", "Rachid", "2018-09-01", "68000", "198712345678" },
                    { "M002", "", "KADIm;I", "Nadia",   "2022-02-15", "54000", "299011122233" },
                };
                for (int r = 0; r < 2; r++) for (int cc = 0; cc < headers.Length; cc++) ws.Cell(r + 2, cc + 1).Value = data[r, cc].ToString();
                wb.SaveAs(file);
            }

            ImportTable table = io.Read(file);
            // The user's mapping (what the mapping UI would produce after they pick columns).
            var mapping = new Dictionary<string, string>
            {
                [EmployeeImportColumns.LastNameFr] = "Nom",
                [EmployeeImportColumns.FirstNameFr] = "Prénom",
                [EmployeeImportColumns.HireDate] = "Entrée société",
                [EmployeeImportColumns.BaseSalary] = "Paie mensuelle",
                [EmployeeImportColumns.Nss] = "N° CNAS",
            };

            ImportPreview preview = io.BuildPreview(companyId, table, mapping);
            Assert.That(preview.MissingColumns, Is.Empty, "all required fields were mapped");
            Assert.That(preview.NewCount, Is.EqualTo(2));
            Assert.That(preview.ErrorCount, Is.EqualTo(0));

            Result<ImportSummary> result = io.Commit(companyId, table, mapping, DuplicateResolution.SkipDuplicates, Path.GetFileName(file));
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(result.Value.Created, Is.EqualTo(2));
            Assert.That(employees.GetByCompany(companyId, true).Any(e => e.LastNameFr == "DJELLOUL" && e.BaseSalary == 68000m), Is.True);

            TestContext.Out.WriteLine("Custom-header file : " + Path.GetFullPath(file));
            TestContext.Out.WriteLine("Imported via mapping: " + result.Value.Created + " employees");
        }

        private static void Seed(EmployeeService svc, long companyId, string last, string first, string birth, string salary, string nss)
        {
            svc.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = last, FirstNameFr = first,
                Gender = Gender.Male, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.BankTransfer,
                ContractType = ContractType.Cdi, Nss = nss, BirthDate = DateTime.Parse(birth),
                HireDate = new DateTime(2021, 1, 4), BaseSalary = decimal.Parse(salary, System.Globalization.CultureInfo.InvariantCulture),
                IsActive = true
            });
        }

        private static ImportTable AppendPerson(ImportTable t, EmployeeImportExportService io, string last, string first, string birth, string salary, string nss)
        {
            var map = new Dictionary<string, string>
            {
                [EmployeeImportColumns.LastNameFr] = last,
                [EmployeeImportColumns.FirstNameFr] = first,
                [EmployeeImportColumns.BirthDate] = birth,
                [EmployeeImportColumns.HireDate] = "2023-03-01",
                [EmployeeImportColumns.BaseSalary] = salary,
                [EmployeeImportColumns.Nss] = nss,
            };
            var row = io.Columns.Select(c => map.TryGetValue(c.Key, out string v) ? v : string.Empty).ToList();
            var rows = t.Rows.ToList();
            rows.Add(row);
            return new ImportTable(t.Headers, rows);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln"))) dir = dir.Parent;
            return dir?.FullName ?? TestContext.CurrentContext.TestDirectory;
        }
    }
}
