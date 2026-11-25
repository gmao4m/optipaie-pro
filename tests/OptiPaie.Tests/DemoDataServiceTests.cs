using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using NUnit.Framework;
using OptiPaie.Common.Configuration;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;
using OptiPaie.Data.Backup;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Removal of the trial demo company, on a temp database seeded by the REAL <see cref="DemoDataSeeder"/>
    /// plus a real customer company with its own employees and payroll. Proves: strict identification
    /// (flag, else full fingerprint; a same-named real company never matches), administrator-only,
    /// refusal when the demo is the only company, a backup BEFORE deletion (and the backup holds the demo),
    /// every dependent table emptied with no orphan left, the real company untouched byte for byte,
    /// global rows kept, settings pointing at the demo moved, and a failure rolls everything back.
    /// </summary>
    [TestFixture]
    public sealed class DemoDataServiceTests
    {
        private string _dir;
        private AppConfiguration _config;
        private SqliteConnectionFactory _factory;
        private IUnitOfWorkFactory _uowf;
        private ICompanyService _companies;
        private IEmployeeService _employees;
        private BackupService _backup;
        private long _demoId;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-demo-removal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _config = new AppConfiguration(_dir, "fr");

            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(_config.DatabaseFilePath);
            using (var connection = _factory.CreateOpenConnection()) new MigrationRunner(connection).Run();

            _uowf = new UnitOfWorkFactory(_factory);
            IAccessContext all = AccessContext.Unrestricted;
            _companies = new CompanyService(_uowf, new CompanyValidator(), all);
            _employees = new EmployeeService(_uowf, new EmployeeValidator(), all);
            var seeder = new DemoDataSeeder(_companies, _employees, new ContractService(_uowf, all),
                new AttendanceService(_uowf, all), new LeaveService(_uowf, all), new LoanService(_uowf, all),
                new AssetService(_uowf, all), new TrainingService(_uowf, all), new WorkCertificateService(_uowf, all),
                new PerformanceService(_uowf, all));
            Result<long> seeded = seeder.Seed();
            Assert.That(seeded.IsSuccess, Is.True, seeded.Error);
            _demoId = seeded.Value;

            _backup = new BackupService(new SqliteBackupProvider(_factory, _config), _uowf, _config, new NullLogger(), all);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private DemoDataService Service(IAccessContext access = null) =>
            new DemoDataService(_uowf, _backup, access ?? AccessContext.Unrestricted);

        // ------------------------------------------------------------------ identification

        [Test]
        public void Seeder_FlagsTheDemo_AndTheFingerprintStillMatchesTheSeeder()
        {
            DemoCompanyInfo demo = Service().FindDemoCompany();
            Assert.That(demo, Is.Not.Null);
            Assert.That(demo.Id, Is.EqualTo(_demoId));
            Assert.That(demo.MatchedBy, Is.EqualTo("flag"));

            // An older database (seeded before the flag existed): recognised by the FULL fingerprint.
            Exec("UPDATE Companies SET IsDemo = 0 WHERE Id = @id;", new { id = _demoId });
            demo = Service().FindDemoCompany();
            Assert.That(demo, Is.Not.Null, "the seeder's identifiers + roster must still match DemoFingerprint");
            Assert.That(demo.MatchedBy, Is.EqualTo("fingerprint"));
        }

        [Test]
        public void ARealCompanyWithTheSameName_IsNeverTheDemo()
        {
            // Same name, even the same 20 people — but its own legal identifiers and no flag: NOT the demo.
            Exec("UPDATE Companies SET IsDemo = 0, Nif = '001216098765432', Nis = '001216098765432', Rc = '16/00-0012345 B 21', " +
                 "ArticleImposition = '16211234567' WHERE Id = @id;", new { id = _demoId });
            Assert.That(Service().FindDemoCompany(), Is.Null, "name + roster without the seeded identifiers never matches");
        }

        [Test]
        public void ARealCompanyWithTheSameName_NextToTheDemo_OnlyTheDemoMatches()
        {
            Exec("UPDATE Companies SET IsDemo = 0 WHERE Id = @id;", new { id = _demoId });
            long real = AddRealCompany("SARL Atlas Industrie", new[] { "BENALI|Karim", "MANSOURI|Leila" });
            DemoCompanyInfo demo = Service().FindDemoCompany();
            Assert.That(demo.Id, Is.EqualTo(_demoId));
            Assert.That(demo.Id, Is.Not.EqualTo(real));
        }

        [Test]
        public void ADemoTheUserEdited_IsStillRecognised()
        {
            // Seen on a real database: the trial user changed the CNAS number; the app added "Générale".
            Exec("UPDATE Companies SET IsDemo = 0, CnasEmployerNumber = '1234567890', Email = 'moi@exemple.dz', NameFr = 'Ma société test' WHERE Id = @id;",
                new { id = _demoId });
            Exec("INSERT INTO Departments (CompanyId, Name, CreatedAtUtc) VALUES (@id, 'Générale', '2026-01-01');", new { id = _demoId });
            DemoCompanyInfo demo = Service().FindDemoCompany();
            Assert.That(demo, Is.Not.Null);
            Assert.That(demo.MatchedBy, Is.EqualTo("fingerprint"));
        }

        [Test]
        public void ADemoWithAForeignDepartment_IsLeftAlone()
        {
            Exec("INSERT INTO Departments (CompanyId, Name, CreatedAtUtc) VALUES (@id, 'Logistique', '2026-01-01');", new { id = _demoId });
            Assert.That(Service().FindDemoCompany(), Is.Null);
        }

        [Test]
        public void ADemoWithARealEmployeeAdded_IsLeftAlone_EvenWithTheFlag()
        {
            // The customer used the demo company for real: a person who is not in the seeded roster.
            AddEmployee(_demoId, "MANSOURI", "Leila", "Production");
            Assert.That(Service().FindDemoCompany(), Is.Null);
            Assert.That(Service().Preview(), Is.Null);
            Assert.That(Service().Delete().IsFailure, Is.True);
            Assert.That(Count("SELECT COUNT(*) FROM Companies WHERE Id = @id", new { id = _demoId }), Is.EqualTo(1));
        }

        [Test]
        public void CopiedIdentifiersWithOtherEmployees_IsNotTheDemo()
        {
            Exec("UPDATE Companies SET IsDemo = 0 WHERE Id = @id;", new { id = _demoId });
            Exec("UPDATE Employees SET LastNameFr = 'AUTRE' WHERE CompanyId = @id AND LastNameFr = 'BENALI';", new { id = _demoId });
            Assert.That(Service().FindDemoCompany(), Is.Null);
        }

        // ------------------------------------------------------------------ rules

        [Test]
        public void OnlyCompany_IsRefused_NothingDeleted_NoBackupTaken()
        {
            DemoDeletionPreview preview = Service().Preview();
            Assert.That(preview.IsOnlyCompany, Is.True);

            string before = DatabaseHash();
            Result<DemoDeletionOutcome> result = Service().Delete();

            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.ErrorCode, Is.EqualTo("Demo_OnlyCompany"));
            Assert.That(result.Error, Does.Contain("أنشئ شركتك أوّلًا"));
            Assert.That(DatabaseHash(), Is.EqualTo(before), "not one byte changed");
            Assert.That(Directory.Exists(_config.BackupDirectory) ? Directory.GetFiles(_config.BackupDirectory).Length : 0, Is.EqualTo(0));
        }

        [Test]
        public void NonAdministrator_IsDenied()
        {
            AddRealCompany("Entreprise Réelle", new[] { "MANSOURI|Leila" });
            var hr = new FakeAccess { IsEnforced = true, CurrentRole = UserRole.Hr };
            Assert.Throws<AccessDeniedException>(() => Service(hr).Delete());
            Assert.That(Count("SELECT COUNT(*) FROM Companies WHERE Id = @id", new { id = _demoId }), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ the removal

        [Test]
        public void Delete_BacksUpFirst_RemovesEveryDependentRow_LeavesNoOrphan_AndKeepsTheRealCompany()
        {
            long real = AddRealCompany("Entreprise Réelle", new[] { "MANSOURI|Leila", "HADJ|Omar" });
            AddPayroll(_demoId, 1000);
            AddPayroll(real, 2000);
            // A real-company candidate hired as a demo employee: keeps its record, loses only the dangling link.
            long demoEmployee = Count("SELECT MIN(Id) FROM Employees WHERE CompanyId = @id", new { id = _demoId });
            Exec("INSERT INTO JobPostings (Id, CompanyId, Title, OpenDate, CreatedAtUtc) VALUES (9001, @real, 'Comptable', '2026-01-01', '2026-01-01');", new { real });
            Exec("INSERT INTO Candidates (Id, PostingId, FirstName, LastName, HiredEmployeeId, AppliedDate, CreatedAtUtc) VALUES (9001, 9001, 'X', 'Y', @e, '2026-01-01', '2026-01-01');", new { e = demoEmployee });
            Exec("INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue, CreatedAtUtc) VALUES (@k, @v, '2026-01-01');", new { k = SettingKeys.DefaultCompanyId, v = _demoId.ToString(CultureInfo.InvariantCulture) });
            Exec("INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue, CreatedAtUtc) VALUES ('RECENT_COMPANY', @v, '2026-01-01');",
                new { v = "[{\"Id\":" + _demoId + ",\"Name\":\"SARL Atlas Industrie\"},{\"Id\":" + real + ",\"Name\":\"Entreprise Réelle\"}]" });

            long globalLeaveTypes = Count("SELECT COUNT(*) FROM LeaveTypes WHERE CompanyId IS NULL");
            string realBefore = RealCompanyHash(real);
            DemoDeletionPreview preview = Service().Preview();
            Assert.That(preview.IsOnlyCompany, Is.False);
            Assert.That(preview.Count("Employees"), Is.EqualTo(20));
            Assert.That(preview.Count("Payslips"), Is.GreaterThan(0));
            Assert.That(preview.Count("Companies"), Is.EqualTo(1));

            Result<DemoDeletionOutcome> result = Service().Delete();
            Assert.That(result.IsSuccess, Is.True, result.Error);

            // 1. The backup exists and still holds the whole demo (restorable).
            Assert.That(File.Exists(result.Value.BackupPath), Is.True);
            using (var b = new SQLiteConnection("Data Source=" + result.Value.BackupPath + ";Version=3;Read Only=True;"))
            {
                b.Open();
                Assert.That(b.ExecuteScalar<long>("SELECT COUNT(*) FROM Employees WHERE CompanyId = @id", new { id = _demoId }), Is.EqualTo(20));
                Assert.That(b.ExecuteScalar<long>("SELECT COUNT(*) FROM Companies WHERE Id = @id", new { id = _demoId }), Is.EqualTo(1));
            }

            // 2. Exactly what the preview announced was deleted.
            foreach (TableRows row in preview.Rows)
                Assert.That(result.Value.Deleted.Single(d => d.Table == row.Table).Rows, Is.EqualTo(row.Rows), row.Table);

            // 3. Nothing of the demo is left in ANY plan table, and no orphan anywhere.
            Assert.That(Service().FindDemoCompany(), Is.Null);
            foreach (TableRows row in CountsFor(_demoId))
                Assert.That(row.Rows, Is.EqualTo(0), row.Table + " still references the demo");
            Assert.That(Orphans(), Is.Empty);

            // 4. The real company is byte-identical; global rows kept; the hired link detached, the candidate kept.
            Assert.That(RealCompanyHash(real), Is.EqualTo(realBefore));
            Assert.That(Count("SELECT COUNT(*) FROM LeaveTypes WHERE CompanyId IS NULL"), Is.EqualTo(globalLeaveTypes));
            Assert.That(Count("SELECT COUNT(*) FROM Candidates WHERE Id = 9001 AND HiredEmployeeId IS NULL"), Is.EqualTo(1));

            // 5. Settings no longer point at the demo.
            Assert.That(Scalar("SELECT SettingValue FROM AppSettings WHERE SettingKey = @k", new { k = SettingKeys.DefaultCompanyId }),
                Is.EqualTo(real.ToString(CultureInfo.InvariantCulture)));
            Assert.That(Scalar("SELECT SettingValue FROM AppSettings WHERE SettingKey = 'RECENT_COMPANY'"), Does.Not.Contain("\"Id\":" + _demoId + ","));
            Assert.That(result.Value.RemainingCompanyId, Is.EqualTo(real));
        }

        [Test]
        public void EveryTableThatReferencesACompanyOrAnEmployee_IsInTheDeletionPlan()
        {
            var planned = new HashSet<string>(CountsFor(_demoId).Select(r => r.Table));
            var missing = new List<string>();
            using (var c = _factory.CreateOpenConnection())
            {
                foreach (string table in c.Query<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"))
                {
                    var cols = c.Query<string>("SELECT name FROM pragma_table_info(@t);", new { t = table }).ToList();
                    if (cols.Contains("CompanyId") || cols.Contains("EmployeeId")) if (!planned.Contains(table)) missing.Add(table);
                }
            }
            Assert.That(missing, Is.Empty, "tables holding company/employee rows that the removal would leave behind");
        }

        [Test]
        public void AFailureMidway_RollsEverythingBack()
        {
            AddRealCompany("Entreprise Réelle", new[] { "MANSOURI|Leila" });
            Exec("CREATE TRIGGER block_company_delete BEFORE DELETE ON Companies BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
            // BackupRecords is excluded: the pre-deletion backup legitimately records itself.
            string before = DatabaseHash(new[] { "BackupRecords" });

            Result<DemoDeletionOutcome> result = Service().Delete();

            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.ErrorCode, Is.EqualTo("Demo_DeleteFailed"));
            Assert.That(DatabaseHash(new[] { "BackupRecords" }), Is.EqualTo(before), "all-or-nothing");
            Assert.That(Count("SELECT COUNT(*) FROM Employees WHERE CompanyId = @id", new { id = _demoId }), Is.EqualTo(20));
        }

        // ------------------------------------------------------------------ helpers

        private long AddRealCompany(string name, IEnumerable<string> people)
        {
            Result<long> created = _companies.Create(new Company
            {
                NameFr = name, Nif = "001234567890123", Nis = "001234567890123", AddressFr = "Rue Didouche Mourad, Alger"
            });
            Assert.That(created.IsSuccess, Is.True, created.Error);
            foreach (string p in people)
            {
                string[] parts = p.Split('|');
                AddEmployee(created.Value, parts[0], parts[1], "Comptabilité");
            }
            return created.Value;
        }

        private void AddEmployee(long companyId, string last, string first, string department)
        {
            Result<long> r = _employees.Create(new Employee
            {
                CompanyId = companyId, LastNameFr = last, FirstNameFr = first, Department = department,
                BaseSalary = 50000m, HireDate = new DateTime(2024, 1, 1), IsActive = true,
                Gender = Gender.Female, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash, ContractType = ContractType.Cdi
            });
            Assert.That(r.IsSuccess, Is.True, r.Error);
        }

        /// <summary>A saved payroll month for every employee of the company: run, payslip, one line, one archive.</summary>
        private void AddPayroll(long companyId, long idBase)
        {
            Exec("INSERT INTO PayrollRuns (Id, CompanyId, PeriodYear, PeriodMonth, CreatedAtUtc) VALUES (@id, @c, 2026, 10, '2026-10-31');",
                new { id = idBase, c = companyId });
            long n = idBase;
            foreach (long e in _factory.CreateOpenConnection().Query<long>("SELECT Id FROM Employees WHERE CompanyId = @c", new { c = companyId }).ToList())
            {
                n++;
                Exec("INSERT INTO Payslips (Id, RunId, EmployeeId, SalaireBrut, NetSalaire, GeneratedAtUtc, CreatedAtUtc, Status) " +
                     "VALUES (@p, @r, @e, '50000', '42000', '2026-10-31', '2026-10-31', 1);", new { p = n, r = idBase, e });
                Exec("INSERT INTO PayrollDetails (PayslipId, LabelFr, ElementType, CreatedAtUtc) VALUES (@p, 'Salaire de base', 1, '2026-10-31');", new { p = n });
                Exec("INSERT INTO ArchiveDocuments (PayslipId, LanguageCode, CreatedAtUtc) VALUES (@p, 'fr', '2026-10-31');", new { p = n });
            }
        }

        private IReadOnlyList<string> Orphans()
        {
            var found = new List<string>();
            var parents = new Dictionary<string, string>
            {
                ["CompanyId"] = "Companies", ["EmployeeId"] = "Employees", ["HiredEmployeeId"] = "Employees",
                ["PayslipId"] = "Payslips", ["RunId"] = "PayrollRuns", ["LoanId"] = "Loans", ["CandidateId"] = "Candidates",
                ["PostingId"] = "JobPostings", ["SessionId"] = "TrainingSessions", ["AssetId"] = "Assets",
                ["EvaluationId"] = "Evaluations"
            };
            using (var c = _factory.CreateOpenConnection())
            {
                found.AddRange(c.Query<string>("SELECT \"table\" || ' -> ' || parent FROM pragma_foreign_key_check;"));
                foreach (string table in c.Query<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';").ToList())
                {
                    foreach (string col in c.Query<string>("SELECT name FROM pragma_table_info(@t);", new { t = table }).ToList())
                    {
                        if (!parents.TryGetValue(col, out string parent) || parent == table) continue;
                        long n = c.ExecuteScalar<long>("SELECT COUNT(*) FROM " + table + " WHERE " + col + " IS NOT NULL AND " + col + " <> 0 AND " +
                                                       col + " NOT IN (SELECT Id FROM " + parent + ");");
                        if (n > 0) found.Add(table + "." + col + " -> " + parent + " : " + n);
                    }
                }
            }
            return found;
        }

        /// <summary>Every row that belongs to the real company, across the tables it can own.</summary>
        private string RealCompanyHash(long real)
        {
            const string E = "(SELECT Id FROM Employees WHERE CompanyId = @c)";
            var sb = new StringBuilder();
            using (var c = _factory.CreateOpenConnection())
            {
                foreach (string sql in new[]
                {
                    "SELECT * FROM Companies WHERE Id = @c",
                    "SELECT * FROM Employees WHERE CompanyId = @c ORDER BY Id",
                    "SELECT * FROM Departments WHERE CompanyId = @c ORDER BY Id",
                    "SELECT * FROM EmploymentContracts WHERE EmployeeId IN " + E + " ORDER BY Id",
                    "SELECT * FROM PayrollRuns WHERE CompanyId = @c ORDER BY Id",
                    "SELECT * FROM Payslips WHERE EmployeeId IN " + E + " ORDER BY Id",
                    "SELECT * FROM PayrollDetails WHERE PayslipId IN (SELECT Id FROM Payslips WHERE EmployeeId IN " + E + ") ORDER BY Id",
                    "SELECT * FROM ArchiveDocuments WHERE PayslipId IN (SELECT Id FROM Payslips WHERE EmployeeId IN " + E + ") ORDER BY Id",
                    "SELECT * FROM JobPostings WHERE CompanyId = @c ORDER BY Id",
                    "SELECT Id, PostingId, FirstName, LastName FROM Candidates ORDER BY Id",
                })
                {
                    foreach (IDictionary<string, object> row in c.Query(sql, new { c = real }).Cast<IDictionary<string, object>>())
                        sb.AppendLine(string.Join("|", row.Select(kv => kv.Key + "=" + Convert.ToString(kv.Value, CultureInfo.InvariantCulture))));
                }
            }
            return Sha(sb.ToString());
        }

        private string DatabaseHash(IEnumerable<string> skip = null)
        {
            var skipped = new HashSet<string>(skip ?? Enumerable.Empty<string>());
            var sb = new StringBuilder();
            using (var c = _factory.CreateOpenConnection())
            {
                foreach (string table in c.Query<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;").ToList())
                {
                    if (skipped.Contains(table)) continue;
                    sb.AppendLine("#" + table);
                    foreach (IDictionary<string, object> row in c.Query("SELECT * FROM " + table + " ORDER BY rowid;").Cast<IDictionary<string, object>>())
                        sb.AppendLine(string.Join("|", row.Select(kv => Convert.ToString(kv.Value, CultureInfo.InvariantCulture))));
                }
            }
            return Sha(sb.ToString());
        }

        private IReadOnlyList<TableRows> CountsFor(long companyId)
        {
            using (IUnitOfWork uow = _uowf.Create()) return uow.DemoData.CountDependents(companyId);
        }

        private static string Sha(string s)
        {
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }

        private void Exec(string sql, object args = null)
        {
            using (var c = _factory.CreateOpenConnection()) c.Execute(sql, args);
        }

        private long Count(string sql, object args = null)
        {
            using (var c = _factory.CreateOpenConnection()) return c.ExecuteScalar<long>(sql, args);
        }

        private string Scalar(string sql, object args = null)
        {
            using (var c = _factory.CreateOpenConnection()) return c.ExecuteScalar<string>(sql, args);
        }

        private sealed class FakeAccess : IAccessContext
        {
            public bool IsEnforced { get; set; }
            public UserRole CurrentRole { get; set; }
            public bool IsOwner { get; set; }
            public long? CurrentUserId { get; set; }
        }

        private sealed class NullLogger : OptiPaie.Common.Logging.ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }
}
